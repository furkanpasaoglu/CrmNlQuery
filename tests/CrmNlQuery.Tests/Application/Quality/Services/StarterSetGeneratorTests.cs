using System.Xml.Linq;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Application.Quality.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Quality.Services;

/// <summary>
/// Tests the schema-based starter set generator: templates, skipping by facts, excluding sensitive and system fields, language.
/// </summary>
public class StarterSetGeneratorTests
{
    private static AttributeInfo Pick(string name, string label, string type, params (int, string)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Item1, o.Item2)).ToList());

    private static EntityInfo Incident(params AttributeInfo[] extra)
    {
        var attributes = new List<AttributeInfo>
        {
            new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
            new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
            Pick("prioritycode", "Priority", "Picklist", (1, "High"), (2, "Normal"), (3, "Low")),
            Pick("statecode", "Status", "State", (0, "Active"), (1, "Resolved")),
            new AttributeInfo("estimatedvalue", "Estimated Value", "Money", new List<OptionInfo>()),
            new AttributeInfo("customerid", "Customer", "Customer", new List<OptionInfo>()),
            new AttributeInfo("ownerid", "Owner", "Owner", new List<OptionInfo>()),
            new AttributeInfo("versionnumber", "Version Number", "BigInt", new List<OptionInfo>()),
        };
        attributes.AddRange(extra);
        var relationships = new List<RelationshipInfo>
        {
            new RelationshipInfo("incident_customer_accounts", "incident", "customerid", "account", "accountid"),
            new RelationshipInfo("owner_incidents", "incident", "ownerid", "systemuser", "systemuserid"),
        };
        return new EntityInfo("incident", "incidents", "Case", "incidentid", "title", attributes, relationships);
    }

    private static StarterFacts Facts(int total = 45, int filled = 40, bool twoPriorities = true)
    {
        var priority = twoPriorities
            ? new Dictionary<int, int> { [1] = 5, [2] = 30, [3] = 10 }
            : new Dictionary<int, int> { [1] = 0, [2] = 45, [3] = 0 };
        return new StarterFacts(
            new Dictionary<string, int> { ["incident"] = total },
            new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                ["incident.prioritycode"] = priority,
                ["incident.statecode"] = new Dictionary<int, int> { [0] = 42, [1] = 3 },
            },
            new Dictionary<string, int> { ["incident.estimatedvalue"] = filled },
            new Dictionary<string, int> { ["incident.customerid"] = 12 });
    }

    private static List<StarterCase> Generate(EntityInfo entity, StarterFacts facts, string language = "en")
        => StarterSetGenerator.Generate(new[] { entity }, facts, language).ToList();

    /// <summary>
    /// Verifies that a busy table gets all seven templates.
    /// </summary>
    [Fact]
    public void ABusyTable_GetsAllSevenTemplates()
    {
        var cases = Generate(Incident(), Facts());

        Assert.Equal(new[] { "count", "filter", "breakdown", "top", "bottom", "lookup" }.OrderBy(x => x), cases.Select(c => c.Template).Where(t => t != "list").OrderBy(x => x));
        Assert.Contains(cases, c => c.Template == "list");
    }

    /// <summary>
    /// Verifies that every reference is well formed XML and grouped ones compare only the measure.
    /// </summary>
    [Fact]
    public void EveryReference_IsWellFormedXml_AndGroupedOnesCompareOnlyTheMeasure()
    {
        foreach (var c in Generate(Incident(), Facts()))
        {
            var root = XDocument.Parse(c.FetchXml).Root!;
            Assert.Equal("incident", (string?)root.Element("entity")!.Attribute("name"));
            if (c.Template is "breakdown" or "lookup") { Assert.Equal("last", c.Columns); }
            if (c.Template is "top" or "bottom") { Assert.Equal("boundary", c.Ties); }
        }
    }

    /// <summary>
    /// Verifies that top and bottom exclude nulls and order in the right direction.
    /// </summary>
    [Fact]
    public void TopAndBottom_ExcludeNulls_AndOrderInTheRightDirection()
    {
        var cases = Generate(Incident(), Facts());
        var top = XDocument.Parse(cases.Single(c => c.Template == "top").FetchXml).Root!;
        var bottom = XDocument.Parse(cases.Single(c => c.Template == "bottom").FetchXml).Root!;

        Assert.Equal("5", (string?)top.Attribute("top"));
        Assert.Equal("true", (string?)top.Descendants("order").Single().Attribute("descending"));
        Assert.Equal("3", (string?)bottom.Attribute("top"));
        Assert.Null(bottom.Descendants("order").Single().Attribute("descending"));
        Assert.Contains(top.Descendants("condition"), c => (string?)c.Attribute("operator") == "not-null" && (string?)c.Attribute("attribute") == "estimatedvalue");
    }

    /// <summary>
    /// Verifies that system and sensitive fields never become questions.
    /// </summary>
    [Fact]
    public void SystemAndSensitiveFields_NeverBecomeQuestions()
    {
        var sensitive = new AttributeInfo("new_maas", "Salary", "Money", new List<OptionInfo>());
        var gov = Pick("new_governmentid", "Government ID", "Picklist", (1, "A"), (2, "B"));
        var cases = Generate(Incident(sensitive, gov), Facts());
        var text = string.Join("\n", cases.Select(c => c.Question + c.FetchXml));

        Assert.DoesNotContain("new_maas", text);
        Assert.DoesNotContain("Salary", text);
        Assert.DoesNotContain("new_governmentid", text);
        Assert.DoesNotContain("versionnumber", text);
        Assert.DoesNotContain("ownerid", text);
    }

    /// <summary>
    /// Verifies that technical setting fields are not ranked as business questions.
    /// </summary>
    [Fact]
    public void TechnicalSettingFields_AreNotRankedAsBusinessQuestions()
    {
        var technical = new AttributeInfo("quantitydecimal", "Decimals Supported", "Integer", new List<OptionInfo>());
        var facts = Facts() with { FilledCounts = new Dictionary<string, int> { ["incident.quantitydecimal"] = 45, ["incident.estimatedvalue"] = 40 } };
        var cases = Generate(Incident(technical), facts);

        Assert.DoesNotContain(cases, c => c.FetchXml.Contains("quantitydecimal") || c.Question.Contains("Decimals Supported"));
        Assert.Contains(cases, c => c.Template == "top" && c.FetchXml.Contains("estimatedvalue"));
    }

    /// <summary>
    /// Verifies that derived totals are not ranked but the business amount is.
    /// </summary>
    [Fact]
    public void DerivedTotals_AreNotRankedButTheBusinessAmountIs()
    {
        var discount = new AttributeInfo("totaldiscountamount", "Total Discount Amount", "Money", new List<OptionInfo>());
        var facts = Facts() with { FilledCounts = new Dictionary<string, int> { ["incident.totaldiscountamount"] = 45, ["incident.estimatedvalue"] = 30 } };
        var cases = Generate(Incident(discount), facts);

        Assert.DoesNotContain(cases, c => c.FetchXml.Contains("totaldiscountamount"));
        Assert.Contains(cases, c => c.Template == "top" && c.FetchXml.Contains("estimatedvalue"));
    }

    /// <summary>
    /// Verifies that turkish ranking questions read naturally.
    /// </summary>
    [Fact]
    public void TurkishRankingQuestions_ReadNaturally()
    {
        var tr = Generate(Incident(), Facts(), "tr");

        Assert.Contains(tr, c => c.Template == "top" && c.Question == "Case kayıtlarından Estimated Value değeri en yüksek 5 tanesini getir");
        Assert.Contains(tr, c => c.Template == "bottom" && c.Question == "Case kayıtlarından Estimated Value değeri en düşük 3 tanesini getir");
    }

    /// <summary>
    /// Verifies that an empty table generates nothing.
    /// </summary>
    [Fact]
    public void AnEmptyTable_GeneratesNothing()
    {
        Assert.Empty(Generate(Incident(), Facts(total: 0)));
    }

    /// <summary>
    /// Verifies that big tables skip the full list.
    /// </summary>
    [Fact]
    public void BigTables_SkipTheFullList()
    {
        var cases = Generate(Incident(), Facts(total: 5000));

        Assert.DoesNotContain(cases, c => c.Template == "list");
        Assert.Contains(cases, c => c.Template == "count");
    }

    /// <summary>
    /// Verifies that an option field with only one used value is not a filter or breakdown candidate.
    /// </summary>
    [Fact]
    public void AnOptionFieldWithOnlyOneUsedValue_IsNotAFilterOrBreakdownCandidate()
    {
        var cases = Generate(Incident(), Facts(twoPriorities: false));

        Assert.All(cases.Where(c => c.Template is "filter" or "breakdown"), c => Assert.DoesNotContain("prioritycode", c.FetchXml));
    }

    /// <summary>
    /// Verifies that a numeric field with too few values gives no ranking.
    /// </summary>
    [Fact]
    public void ANumericFieldWithTooFewValues_GivesNoRanking()
    {
        var cases = Generate(Incident(), Facts(filled: 2));

        Assert.DoesNotContain(cases, c => c.Template is "top" or "bottom");
    }

    /// <summary>
    /// Verifies that alookup with a too large or too small group count is skipped.
    /// </summary>
    [Fact]
    public void AlookupWithATooLargeOrTooSmallGroupCount_IsSkipped()
    {
        var one = Facts() with { GroupCounts = new Dictionary<string, int> { ["incident.customerid"] = 1 } };
        var huge = Facts() with { GroupCounts = new Dictionary<string, int> { ["incident.customerid"] = 500 } };

        Assert.DoesNotContain(Generate(Incident(), one), c => c.Template == "lookup");
        Assert.DoesNotContain(Generate(Incident(), huge), c => c.Template == "lookup");
    }

    /// <summary>
    /// Verifies that the questions follow the language and use the exact labels.
    /// </summary>
    [Fact]
    public void TheQuestionsFollowTheLanguage_AndUseTheExactLabels()
    {
        var en = Generate(Incident(), Facts(), "en");
        var tr = Generate(Incident(), Facts(), "tr");

        Assert.Contains(en, c => c.Question == "How many Case records are there?");
        Assert.Contains(tr, c => c.Question == "Case kayıt sayısı kaç?");
        Assert.Contains(en, c => c.Template == "filter" && c.Question.Contains("Priority") && c.Question.Contains("Normal"));
        Assert.Contains(tr, c => c.Template == "breakdown" && c.Question.EndsWith("grafik olarak göster"));
    }

    /// <summary>
    /// Verifies that case IDs are unique and stable.
    /// </summary>
    [Fact]
    public void CaseIds_AreUniqueAndStable()
    {
        var first = Generate(Incident(), Facts()).Select(c => c.Id).ToList();
        var second = Generate(Incident(), Facts()).Select(c => c.Id).ToList();

        Assert.Equal(first.Count, first.Distinct().Count());
        Assert.Equal(first, second);
        Assert.All(first, id => Assert.StartsWith("gen-incident-", id));
    }

    /// <summary>
    /// Verifies that the list and ranking questions force the columns the reference returns.
    /// </summary>
    [Fact]
    public void TheListAndRankingQuestions_ForceTheColumnsTheReferenceReturns()
    {
        var cases = Generate(Incident(), Facts());

        Assert.Equal("custom:title", cases.Single(c => c.Template == "list").AnswerColumns);
        Assert.Equal("custom:title, estimatedvalue", cases.Single(c => c.Template == "top").AnswerColumns);
        Assert.Null(cases.Single(c => c.Template == "breakdown").AnswerColumns);
    }
}
