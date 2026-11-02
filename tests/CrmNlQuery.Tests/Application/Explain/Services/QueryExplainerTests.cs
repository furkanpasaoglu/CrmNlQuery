using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Explain.Services;

/// <summary>
/// Tests that the model-free query explanation matches the FetchXML that actually runs.
/// </summary>
public class QueryExplainerTests
{
    private static AttributeInfo A(string name, string label, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static readonly EntityInfo Opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Opportunity", "Uniqueidentifier"), A("name", "Topic", "String"),
            A("statecode", "Status", "State", (0, "Open"), (1, "Won"), (2, "Lost")),
            A("address1_city", "City", "String"), A("estimatedvalue", "Est. Revenue", "Money"),
            A("createdon", "Created On", "DateTime"), A("customerid", "Potential Customer", "Customer"),
            A("donotemail", "Do not allow Emails", "Boolean", (1, "Yes"), (0, "No")),
            A("numberofemployees", "Employees", "Integer"),
        },
        new List<RelationshipInfo>());

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo>
        {
            A("accountid", "Account", "Uniqueidentifier"), A("name", "Account Name", "String"),
            A("industrycode", "Industry", "Picklist", (1, "Retail"), (2, "Software")),
            A("segment", "Segment", "String"),
        },
        new List<RelationshipInfo>());

    private static QueryExplainer Explainer(DateTimeOffset? now = null)
        => new QueryExplainer(new FakeMetadataSource(Opportunity, Account), () => now ?? new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

    private static string Fetch(string inner, string rootAttrs = "") => $"<fetch{rootAttrs}><entity name=\"opportunity\">{inner}</entity></fetch>";

    private static Task<QueryExplanation?> Explain(string fetchXml, params ResolvedRecord[] resolved)
        => Explainer().ExplainAsync(fetchXml, resolved);

    /// <summary>
    /// Verifies that the entity is named by its label.
    /// </summary>
    [Fact]
    public async Task TheEntity_IsNamedByItsLabel()
    {
        var e = await Explain(Fetch("<attribute name=\"name\" />"));

        Assert.Equal("opportunity", e!.Entity);
        Assert.Equal("Opportunity", e.EntityLabel);
        Assert.Empty(e.Filters);
    }

    /// <summary>
    /// Verifies that an option set condition is shown with field and option labels.
    /// </summary>
    [Fact]
    public async Task AnOptionSetCondition_IsShownWithFieldAndOptionLabels()
    {
        var e = await Explain(Fetch("<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>"));

        var f = Assert.Single(e!.Filters);
        Assert.Equal("f1", f.Id);
        Assert.Equal("statecode", f.Field);
        Assert.Equal("Status", f.FieldLabel);
        Assert.Equal("eq", f.Op);
        Assert.Equal("Open", Assert.Single(f.Values).Label);
        Assert.Equal("0", f.Values[0].Value);
    }

    /// <summary>
    /// Verifies that a boolean condition is shown with its option label.
    /// </summary>
    [Fact]
    public async Task ABooleanCondition_IsShownWithItsOptionLabel()
    {
        var e = await Explain(Fetch("<filter><condition attribute=\"donotemail\" operator=\"eq\" value=\"1\" /></filter>"));

        Assert.Equal("Yes", e!.Filters[0].Values[0].Label);
    }

    /// <summary>
    /// Verifies that an unknown field falls back to its logical name without failing.
    /// </summary>
    [Fact]
    public async Task AnUnknownField_FallsBackToItsLogicalName_WithoutFailing()
    {
        var e = await Explain(Fetch("<filter><condition attribute=\"new_secret\" operator=\"eq\" value=\"x\" /></filter>"));

        Assert.Equal("new_secret", Assert.Single(e!.Filters).FieldLabel);
    }

    /// <summary>
    /// Verifies that like patterns become readable operators.
    /// </summary>
    [Theory]
    [InlineData("%ist%", "contains", "ist")]
    [InlineData("ist%", "begins-with", "ist")]
    [InlineData("%ist", "ends-with", "ist")]
    [InlineData("istanbul", "like", "istanbul")]
    public async Task LikePatterns_BecomeReadableOperators(string pattern, string op, string shown)
    {
        var e = await Explain(Fetch($"<filter><condition attribute=\"address1_city\" operator=\"like\" value=\"{pattern}\" /></filter>"));

        var f = Assert.Single(e!.Filters);
        Assert.Equal(op, f.Op);
        Assert.Equal(shown, f.Values[0].Value);
    }

    /// <summary>
    /// Verifies that an in condition lists every value with labels.
    /// </summary>
    [Fact]
    public async Task AnInCondition_ListsEveryValue_WithLabels()
    {
        var e = await Explain(Fetch("<filter><condition attribute=\"statecode\" operator=\"in\"><value>0</value><value>1</value></condition></filter>"));

        var f = Assert.Single(e!.Filters);
        Assert.Equal(new[] { "Open", "Won" }, f.Values.Select(v => v.Label));
    }

    /// <summary>
    /// Verifies that null and not null have no values.
    /// </summary>
    [Fact]
    public async Task NullAndNotNull_HaveNoValues()
    {
        var e = await Explain(Fetch("<filter><condition attribute=\"address1_city\" operator=\"null\" /><condition attribute=\"name\" operator=\"not-null\" /></filter>"));

        Assert.Equal(new[] { "null", "not-null" }, e!.Filters.Select(f => f.Op));
        Assert.All(e.Filters, f => Assert.Empty(f.Values));
    }

    /// <summary>
    /// Verifies that a lookup GUID is shown by the resolved record name and an unknown GUID is never shown.
    /// </summary>
    [Fact]
    public async Task ALookupGuid_IsShownByTheResolvedRecordName_AndAnUnknownGuidIsNeverShown()
    {
        var known = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var xml = Fetch($"<filter><condition attribute=\"customerid\" operator=\"eq\" value=\"{known}\" /><condition attribute=\"customerid\" operator=\"in\"><value>{unknown}</value><value>{Guid.NewGuid()}</value></condition></filter>");

        var e = await Explain(xml, new ResolvedRecord("Örnekova", "account", known, "Örnekova A.Ş."));

        Assert.Equal("Örnekova A.Ş.", e!.Filters[0].Values[0].Label);
        Assert.All(e.Filters[1].Values, v => { Assert.Null(v.Value); Assert.Null(v.Label); });
        Assert.Equal(2, e.Filters[1].Values.Count);
    }

    /// <summary>
    /// Verifies that sort and, user limit, and are shown, and but the default five hundred is not.
    /// </summary>
    [Fact]
    public async Task Sort_And_UserLimit_AreShown_ButTheDefaultFiveHundredIsNot()
    {
        var limited = await Explain(Fetch("<attribute name=\"name\" /><order attribute=\"estimatedvalue\" descending=\"true\" />", " top=\"10\""));
        var defaulted = await Explain(Fetch("<attribute name=\"name\" />", " top=\"500\""));

        Assert.Equal(10, limited!.Top);
        var sort = Assert.Single(limited.Sort);
        Assert.Equal("Est. Revenue", sort.Label);
        Assert.True(sort.Descending);
        Assert.Null(defaulted!.Top);
    }

    /// <summary>
    /// Verifies that an aggregate is flagged with its group by labels and without the row ID alias.
    /// </summary>
    [Fact]
    public async Task AnAggregate_IsFlagged_WithItsGroupByLabels_AndWithoutTheRowIdAlias()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"statecode\" groupby=\"true\" alias=\"s\" />" +
                  "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /></entity></fetch>";

        var e = await Explain(xml);

        Assert.True(e!.Aggregate);
        Assert.Equal(new[] { "Status" }, e.GroupBy);
    }

    /// <summary>
    /// Verifies that date grouping shows the unit next to the field label.
    /// </summary>
    [Fact]
    public async Task DateGrouping_ShowsTheUnitNextToTheFieldLabel()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"statecode\" groupby=\"true\" dategrouping=\"month\" alias=\"m\" />" +
                  "<attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /></entity></fetch>";

        var e = await Explain(xml);

        Assert.Equal(new[] { "Status (month)" }, e!.GroupBy);
    }

    /// <summary>
    /// Verifies that a linked entity condition is labelled with the linked tables attributes and via.
    /// </summary>
    [Fact]
    public async Task ALinkedEntityCondition_IsLabelledWithTheLinkedTablesAttributes_AndVia()
    {
        var xml = Fetch("<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"acc\" link-type=\"inner\"><filter><condition attribute=\"industrycode\" operator=\"eq\" value=\"2\" /></filter></link-entity>");

        var e = await Explain(xml);

        var f = Assert.Single(e!.Filters);
        Assert.Equal("Industry", f.FieldLabel);
        Assert.Equal("Software", f.Values[0].Label);
        Assert.Equal("Account", f.Via);
    }

    /// <summary>
    /// Verifies that a root condition with entity name is mapped to the linked table.
    /// </summary>
    [Fact]
    public async Task ARootConditionWithEntityName_IsMappedToTheLinkedTable()
    {
        var xml = Fetch("<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"acc\" link-type=\"outer\" /><filter><condition entityname=\"acc\" attribute=\"segment\" operator=\"eq\" value=\"Small\" /></filter>");

        var f = Assert.Single((await Explain(xml))!.Filters);

        Assert.Equal("Segment", f.FieldLabel);
        Assert.Equal("Account", f.Via);
    }

    /// <summary>
    /// Verifies that conditions inside an or filter share a group but a single condition or filter does not.
    /// </summary>
    [Fact]
    public async Task ConditionsInsideAnOrFilter_ShareAGroup_ButASingleConditionOrFilterDoesNot()
    {
        var shared = await Explain(Fetch("<filter type=\"or\"><condition attribute=\"address1_city\" operator=\"eq\" value=\"A\" /><condition attribute=\"address1_city\" operator=\"eq\" value=\"B\" /></filter>"));
        var single = await Explain(Fetch("<filter type=\"or\"><condition attribute=\"address1_city\" operator=\"eq\" value=\"A\" /></filter>"));

        Assert.Equal(2, shared!.Filters.Count);
        Assert.NotNull(shared.Filters[0].Group);
        Assert.Equal(shared.Filters[0].Group, shared.Filters[1].Group);
        Assert.Null(single!.Filters[0].Group);
    }

    /// <summary>
    /// Verifies that filter IDs are stable and follow document order.
    /// </summary>
    [Fact]
    public async Task FilterIds_AreStableAndFollowDocumentOrder()
    {
        var xml = Fetch("<filter><condition attribute=\"name\" operator=\"eq\" value=\"a\" /></filter><link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"acc\"><filter><condition attribute=\"segment\" operator=\"eq\" value=\"b\" /></filter></link-entity>");

        var first = (await Explain(xml))!.Filters.Select(f => f.Id).ToList();
        var second = (await Explain(xml))!.Filters.Select(f => f.Id).ToList();

        Assert.Equal(new[] { "f1", "f2" }, first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Verifies that an or filter with a nested filter is not explained because flattening it would turn or into and.
    /// </summary>
    [Fact]
    public async Task AnOrFilterWithANestedFilter_IsNotExplained_BecauseFlatteningItWouldTurnOrIntoAnd()
    {
        var xml = Fetch("<filter type=\"or\"><condition attribute=\"address1_city\" operator=\"eq\" value=\"A\" /><filter type=\"and\"><condition attribute=\"name\" operator=\"eq\" value=\"B\" /><condition attribute=\"name\" operator=\"eq\" value=\"C\" /></filter></filter>");

        Assert.Null(await Explain(xml));
    }

    /// <summary>
    /// Verifies that an and filter nested in an and filter is flattened because the meaning is the same.
    /// </summary>
    [Fact]
    public async Task AnAndFilterNestedInAnAndFilter_IsFlattened_BecauseTheMeaningIsTheSame()
    {
        var xml = Fetch("<filter><condition attribute=\"name\" operator=\"eq\" value=\"a\" /><filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"b\" /></filter></filter>");

        var e = await Explain(xml);

        Assert.Equal(2, e!.Filters.Count);
        Assert.All(e.Filters, f => Assert.Null(f.Group));
    }

    /// <summary>
    /// Verifies that aggregate functions are reported with their field labels and a sort on the alias points at the measure.
    /// </summary>
    [Fact]
    public async Task AggregateFunctions_AreReported_WithTheirFieldLabels_AndASortOnTheAliasPointsAtTheMeasure()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"statecode\" groupby=\"true\" alias=\"s\" />" +
                  "<attribute name=\"estimatedvalue\" aggregate=\"sum\" alias=\"total\" /><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
                  "<order alias=\"total\" descending=\"true\" /></entity></fetch>";

        var e = await Explain(xml);

        Assert.Equal(new[] { "sum", "count" }, e!.Measures.Select(m => m.Fn));
        Assert.Equal("Est. Revenue", e.Measures[0].FieldLabel);
        var sort = Assert.Single(e.Sort);
        Assert.Equal("sum", sort.Fn);
        Assert.Equal("Est. Revenue", sort.Label);
    }

    /// <summary>
    /// Verifies that a GUID looking text that is not a GUID is still shown.
    /// </summary>
    [Fact]
    public async Task AGuidLookingTextThatIsNotAGuid_IsStillShown()
    {
        var e = await Explain(Fetch("<filter><condition attribute=\"name\" operator=\"eq\" value=\"0123456789abcdef0123456789abcdef\" /></filter>"));

        Assert.Equal("0123456789abcdef0123456789abcdef", e!.Filters[0].Values[0].Value);
    }

    /// <summary>
    /// Verifies that an option filter is editable as an option with the whole option list.
    /// </summary>
    [Fact]
    public async Task AnOptionFilter_IsEditableAsAnOption_WithTheWholeOptionList()
    {
        var f = Assert.Single((await Explain(Fetch("<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>")))!.Filters);

        Assert.Equal("option", f.Edit!.Kind);
        Assert.Equal(new[] { "Open", "Won", "Lost" }, f.Edit.Options!.Select(o => o.Label));
        Assert.Equal(new[] { "0", "1", "2" }, f.Edit.Options!.Select(o => o.Value));
    }

    /// <summary>
    /// Verifies that a boolean filter is editable as an option.
    /// </summary>
    [Fact]
    public async Task ABooleanFilter_IsEditableAsAnOption()
    {
        var f = Assert.Single((await Explain(Fetch("<filter><condition attribute=\"donotemail\" operator=\"eq\" value=\"1\" /></filter>")))!.Filters);

        Assert.Equal("option", f.Edit!.Kind);
    }

    /// <summary>
    /// Verifies that a numeric comparison is editable as a number.
    /// </summary>
    [Fact]
    public async Task ANumericComparison_IsEditableAsANumber()
    {
        var f = Assert.Single((await Explain(Fetch("<filter><condition attribute=\"estimatedvalue\" operator=\"gt\" value=\"1000\" /></filter>")))!.Filters);

        Assert.Equal("number", f.Edit!.Kind);
        Assert.Null(f.Edit.Options);
    }

    /// <summary>
    /// Verifies that an integer comparison is editable as an integer not a decimal number.
    /// </summary>
    [Fact]
    public async Task AnIntegerComparison_IsEditableAsAnInteger_NotADecimalNumber()
    {
        var f = Assert.Single((await Explain(Fetch("<filter><condition attribute=\"numberofemployees\" operator=\"gt\" value=\"10\" /></filter>")))!.Filters);

        Assert.Equal("integer", f.Edit!.Kind);
    }

    /// <summary>
    /// Verifies that a single value text filter is editable as text.
    /// </summary>
    [Theory]
    [InlineData("eq", "Istanbul")]
    [InlineData("like", "%ist%")]
    [InlineData("like", "ist%")]
    [InlineData("not-like", "%ist%")]
    public async Task ASingleValueTextFilter_IsEditableAsText(string op, string value)
    {
        var f = Assert.Single((await Explain(Fetch($"<filter><condition attribute=\"address1_city\" operator=\"{op}\" value=\"{value}\" /></filter>")))!.Filters);

        Assert.Equal("text", f.Edit!.Kind);
    }

    /// <summary>
    /// Verifies that others are only removable.
    /// </summary>
    [Theory]
    [InlineData("<condition attribute=\"createdon\" operator=\"last-x-days\" value=\"30\" />")]
    [InlineData("<condition attribute=\"statecode\" operator=\"in\"><value>0</value><value>1</value></condition>")]
    [InlineData("<condition attribute=\"address1_city\" operator=\"null\" />")]
    [InlineData("<condition attribute=\"new_unknown\" operator=\"eq\" value=\"x\" />")]
    public async Task Others_AreOnlyRemovable(string condition)
    {
        var f = Assert.Single((await Explain(Fetch("<filter>" + condition + "</filter>")))!.Filters);

        Assert.Null(f.Edit);
    }

    /// <summary>
    /// Verifies that a lookup filter is only removable.
    /// </summary>
    [Fact]
    public async Task ALookupFilter_IsOnlyRemovable()
    {
        var f = Assert.Single((await Explain(Fetch($"<filter><condition attribute=\"customerid\" operator=\"eq\" value=\"{Guid.NewGuid()}\" /></filter>")))!.Filters);

        Assert.Null(f.Edit);
    }

    /// <summary>
    /// Verifies that the conditions helper lists the elements in the same order as the filter IDs.
    /// </summary>
    [Fact]
    public async Task TheConditionsHelper_ListsTheElementsInTheSameOrderAsTheFilterIds()
    {
        var xml = Fetch("<filter><condition attribute=\"name\" operator=\"eq\" value=\"a\" /></filter><link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"acc\"><filter><condition attribute=\"segment\" operator=\"eq\" value=\"b\" /></filter></link-entity>");
        var entity = System.Xml.Linq.XDocument.Parse(xml).Root!.Element("entity")!;

        var elements = QueryExplainer.Conditions(entity);
        var filters = (await Explain(xml))!.Filters;

        Assert.Equal(filters.Count, elements.Count);
        for (var i = 0; i < filters.Count; i++)
        {
            Assert.Equal("f" + (i + 1), filters[i].Id);
            Assert.Equal(filters[i].Field, (string?)elements[i].Attribute("attribute"));
        }
    }

    /// <summary>
    /// Verifies that broken or unknown input gives null and never throws.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not xml <<<")]
    [InlineData("<fetch><entity name=\"unknown_table\" /></fetch>")]
    [InlineData("<fetch />")]
    public async Task BrokenOrUnknownInput_GivesNull_AndNeverThrows(string xml)
    {
        Assert.Null(await Explain(xml));
    }
}
