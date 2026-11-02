using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Steps;

/// <summary>
/// Tests that step ids are filled in by code, never by the model, and that over-limit or empty id lists stop the chain.
/// </summary>
public class StepInputFillerTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static StepInputFiller Filler()
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String") }, new List<RelationshipInfo>());
        var contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname", "String"), A("parentcustomerid", "Customer") },
            new List<RelationshipInfo> { new RelationshipInfo("r", "contact", "parentcustomerid", "account", "accountid") });
        return new StepInputFiller(new PlanValidator(new FakeMetadataSource(account, contact)));
    }

    private static string Guid(int n) => new System.Guid(n, 0, 0, new byte[8]).ToString();

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> Ids(params string[] ids) =>
        new Dictionary<int, IReadOnlyList<string>> { [1] = ids };

    private const string InStep1 =
        "<fetch top=\"500\"><entity name=\"contact\"><attribute name=\"fullname\" /><order attribute=\"fullname\" descending=\"false\" />" +
        "<filter><condition attribute=\"parentcustomerid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

    private static List<string> Values(string fetchXml) =>
        XDocument.Parse(fetchXml).Descendants("condition").Single().Elements("value").Select(v => v.Value).ToList();

    /// <summary>
    /// Verifies that the placeholder is replaced by one value per ID and the rest of the query is kept.
    /// </summary>
    [Fact]
    public async Task ThePlaceholder_IsReplacedByOneValuePerId_AndTheRestOfTheQueryIsKept()
    {
        var result = await Filler().FillAsync(InStep1, Ids(Guid(1), Guid(2), Guid(3)));

        Assert.Equal(FillStatus.Filled, result.Status);
        Assert.Equal(new[] { Guid(1), Guid(2), Guid(3) }, Values(result.FetchXml!));
        Assert.DoesNotContain("{{", result.FetchXml);
        var entity = XDocument.Parse(result.FetchXml!).Root!.Element("entity")!;
        Assert.Equal("fullname", (string?)entity.Element("order")!.Attribute("attribute"));
    }

    /// <summary>
    /// Verifies that an eq placeholder with one ID becomes an eq with the value attribute.
    /// </summary>
    [Fact]
    public async Task AnEqPlaceholder_WithOneId_BecomesAnEqWithTheValueAttribute()
    {
        var xml = InStep1.Replace("operator=\"in\"><value>{{step1}}</value></condition>", "operator=\"eq\" value=\"{{step1}}\" />");

        var result = await Filler().FillAsync(xml, Ids(Guid(7)));

        Assert.Equal(FillStatus.Filled, result.Status);
        var condition = XDocument.Parse(result.FetchXml!).Descendants("condition").Single();
        Assert.Equal("eq", (string?)condition.Attribute("operator"));
        Assert.Equal(Guid(7), (string?)condition.Attribute("value"));
    }

    /// <summary>
    /// Verifies that an eq placeholder with several IDs becomes an in.
    /// </summary>
    [Fact]
    public async Task AnEqPlaceholder_WithSeveralIds_BecomesAnIn()
    {
        var xml = InStep1.Replace("operator=\"in\"><value>{{step1}}</value></condition>", "operator=\"eq\" value=\"{{step1}}\" />");

        var result = await Filler().FillAsync(xml, Ids(Guid(1), Guid(2)));

        Assert.Equal(FillStatus.Filled, result.Status);
        var condition = XDocument.Parse(result.FetchXml!).Descendants("condition").Single();
        Assert.Equal("in", (string?)condition.Attribute("operator"));
        Assert.Equal(new[] { Guid(1), Guid(2) }, condition.Elements("value").Select(v => v.Value).ToArray());
    }

    /// <summary>
    /// Verifies that exactly the limit is accepted and one more stops the chain.
    /// </summary>
    [Fact]
    public async Task ExactlyTheLimit_IsAccepted_OneMoreStopsTheChain()
    {
        var atLimit = Enumerable.Range(1, StepInputFiller.MaxIds).Select(Guid).ToArray();
        var over = Enumerable.Range(1, StepInputFiller.MaxIds + 1).Select(Guid).ToArray();

        var ok = await Filler().FillAsync(InStep1, Ids(atLimit));
        var tooMany = await Filler().FillAsync(InStep1, Ids(over));

        Assert.Equal(FillStatus.Filled, ok.Status);
        Assert.Equal(StepInputFiller.MaxIds, Values(ok.FetchXml!).Count);
        Assert.Equal(FillStatus.TooMany, tooMany.Status);
        Assert.Equal(1, tooMany.Step);
        Assert.Equal(StepInputFiller.MaxIds + 1, tooMany.Count);
        Assert.Null(tooMany.FetchXml);
    }

    /// <summary>
    /// Verifies that no IDs stops the chain and never produces a query.
    /// </summary>
    [Fact]
    public async Task NoIds_StopsTheChain_AndNeverProducesAQuery()
    {
        var result = await Filler().FillAsync(InStep1, Ids());

        Assert.Equal(FillStatus.Empty, result.Status);
        Assert.Equal(1, result.Step);
        Assert.Null(result.FetchXml);
    }

    /// <summary>
    /// Verifies that duplicate IDs are collapsed before counting.
    /// </summary>
    [Fact]
    public async Task DuplicateIds_AreCollapsed_BeforeCounting()
    {
        var many = Enumerable.Range(1, StepInputFiller.MaxIds).Select(Guid).Concat(new[] { Guid(1), Guid(2).ToUpperInvariant() }).ToArray();

        var result = await Filler().FillAsync(InStep1, Ids(many));

        Assert.Equal(FillStatus.Filled, result.Status);
        Assert.Equal(StepInputFiller.MaxIds, Values(result.FetchXml!).Count);
    }

    /// <summary>
    /// Verifies that a query without placeholders is returned validated and unchanged.
    /// </summary>
    [Fact]
    public async Task AQueryWithoutPlaceholders_IsReturnedValidated_AndUnchanged()
    {
        var plain = "<fetch top=\"500\"><entity name=\"contact\"><attribute name=\"fullname\" /></entity></fetch>";

        var result = await Filler().FillAsync(plain, Ids());

        Assert.Equal(FillStatus.Filled, result.Status);
        Assert.Contains("fullname", result.FetchXml);
    }

    /// <summary>
    /// Verifies that a placeholder for a step without ids is an error and not a silent skip.
    /// </summary>
    [Fact]
    public async Task ThePlaceholderOfAStepWithoutIds_IsAnError_NotASilentSkip()
    {
        var xml = InStep1.Replace("{{step1}}", "{{step2}}");

        var result = await Filler().FillAsync(xml, Ids(Guid(1)));

        Assert.Equal(FillStatus.Empty, result.Status);
        Assert.Equal(2, result.Step);
    }

    /// <summary>
    /// Verifies that if the filled query is no longer valid the chain stops with the errors.
    /// </summary>
    [Fact]
    public async Task IfTheFilledQueryIsNoLongerValid_TheChainStopsWithTheErrors()
    {
        var invalid = InStep1.Replace("fullname", "nonexistentfield");

        var result = await Filler().FillAsync(invalid, Ids(Guid(1)));

        Assert.Equal(FillStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Errors);
    }
}
