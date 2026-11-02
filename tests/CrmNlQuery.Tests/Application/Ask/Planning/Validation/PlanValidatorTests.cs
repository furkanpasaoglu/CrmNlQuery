using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests the plan validator rules for entities, attributes, operators, link entities, filters and row caps.
/// </summary>
public class PlanValidatorTests
{
    private static PlanValidator Validator() => new PlanValidator(TestMetadata.Create());

    private static Task<ValidationResult> Check(string fetchXml) => Validator().ValidateFetchXmlAsync(fetchXml);

    /// <summary>
    /// Verifies that valid minimal query passes and gets top500.
    /// </summary>
    [Fact]
    public async Task ValidMinimalQuery_Passes_AndGetsTop500()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"productid\" /><attribute name=\"name\" /></entity></fetch>");
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
        Assert.Equal("500", XDocument.Parse(r.NormalizedFetchXml!).Root!.Attribute("top")!.Value);
    }

    /// <summary>
    /// Verifies that bad top is replaced by500.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("9999")]
    [InlineData("abc")]
    public async Task BadTop_IsReplacedBy500(string top)
    {
        var r = await Check($"<fetch top=\"{top}\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>");
        Assert.True(r.IsValid);
        Assert.Equal("500", XDocument.Parse(r.NormalizedFetchXml!).Root!.Attribute("top")!.Value);
    }

    /// <summary>
    /// Verifies that small top is kept.
    /// </summary>
    [Fact]
    public async Task SmallTop_IsKept()
    {
        var r = await Check("<fetch top=\"10\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>");
        Assert.Equal("10", XDocument.Parse(r.NormalizedFetchXml!).Root!.Attribute("top")!.Value);
    }

    /// <summary>
    /// Verifies that aggregate query does not get top.
    /// </summary>
    [Fact]
    public async Task AggregateQuery_DoesNotGetTop()
    {
        var r = await Check("<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"count\" aggregate=\"count\" /></entity></fetch>");
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
        Assert.Null(XDocument.Parse(r.NormalizedFetchXml!).Root!.Attribute("top"));
    }

    /// <summary>
    /// Verifies that invalid XML fails.
    /// </summary>
    [Fact]
    public async Task InvalidXml_Fails()
    {
        var r = await Check("<fetch><entity name=");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.StartsWith("Invalid XML"));
    }

    /// <summary>
    /// Verifies that unknown entity fails.
    /// </summary>
    [Fact]
    public async Task UnknownEntity_Fails()
    {
        var r = await Check("<fetch><entity name=\"widget\"><attribute name=\"name\" /></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("Unknown entity 'widget'"));
    }

    /// <summary>
    /// Verifies that unknown attribute fails.
    /// </summary>
    [Fact]
    public async Task UnknownAttribute_Fails()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"productname\" /></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("'productname'"));
    }

    /// <summary>
    /// Verifies that all attributes is rejected.
    /// </summary>
    [Fact]
    public async Task AllAttributes_IsRejected()
    {
        var r = await Check("<fetch><entity name=\"product\"><all-attributes /></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("all-attributes"));
    }

    /// <summary>
    /// Verifies that unknown element is rejected.
    /// </summary>
    [Fact]
    public async Task UnknownElement_IsRejected()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><delete /></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("<delete>"));
    }

    /// <summary>
    /// Verifies that invalid option set value lists valid options.
    /// </summary>
    [Fact]
    public async Task InvalidOptionSetValue_ListsValidOptions()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"7\" /></filter></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("'7'") && e.Contains("0=Active") && e.Contains("1=Retired"));
    }

    /// <summary>
    /// Verifies that valid option set value passes.
    /// </summary>
    [Fact]
    public async Task ValidOptionSetValue_Passes()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"1\" /></filter></entity></fetch>");
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Verifies that lookup value that is not GUID fails.
    /// </summary>
    [Fact]
    public async Task LookupValueThatIsNotGuid_Fails()
    {
        var r = await Check("<fetch><entity name=\"contract\"><attribute name=\"title\" /><filter><condition attribute=\"customerid\" operator=\"eq\" value=\"Ornekova\" /></filter></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("GUID"));
    }

    /// <summary>
    /// Verifies that primary ID value that is not GUID fails before the CRM sees it.
    /// </summary>
    [Theory]
    [InlineData("{PRODUCT_GUID}")]
    [InlineData("this product")]
    [InlineData("")]
    public async Task PrimaryIdValueThatIsNotGuid_Fails_BeforeTheCrmSeesIt(string value)
    {
        var r = await Check($"<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><condition attribute=\"productid\" operator=\"eq\" value=\"{value}\" /></filter></entity></fetch>");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("GUID") && e.Contains("productid"));
    }

    /// <summary>
    /// Verifies that primary ID value inside a link and in value elements are checked too.
    /// </summary>
    [Fact]
    public async Task PrimaryIdValueInsideALinkAndInValueElements_AreCheckedToo()
    {
        var link = await Check("<fetch><entity name=\"contractdetail\"><attribute name=\"title\" /><link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"c\">" +
            "<filter><condition attribute=\"contractid\" operator=\"in\"><value>{ID}</value></condition></filter></link-entity></entity></fetch>");
        Assert.Contains(link.Errors, e => e.Contains("GUID"));
    }

    /// <summary>
    /// Verifies that primary ID value with a real GUID passes.
    /// </summary>
    [Fact]
    public async Task PrimaryIdValue_WithARealGuid_Passes()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><condition attribute=\"productid\" operator=\"eq\" value=\"37870e9c-c7c7-f111-b4df-00155d01f303\" /></filter></entity></fetch>");
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Verifies that disallowed operator fails.
    /// </summary>
    [Fact]
    public async Task DisallowedOperator_Fails()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><condition attribute=\"name\" operator=\"drop\" value=\"x\" /></filter></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("Operator 'drop'"));
    }

    private static string WithCondition(string condition) =>
        "<fetch><entity name=\"product\"><attribute name=\"name\" /><filter>" + condition + "</filter></entity></fetch>";

    /// <summary>
    /// Verifies that in operator without values is rejected.
    /// </summary>
    [Fact]
    public async Task InOperatorWithoutValues_IsRejected()
    {
        var r = await Check(WithCondition("<condition attribute=\"productnumber\" operator=\"in\" />"));
        Assert.Contains(r.Errors, e => e.Contains("'in'") && e.Contains("at least one"));
    }

    /// <summary>
    /// Verifies that in operator with empty value list is rejected.
    /// </summary>
    [Fact]
    public async Task InOperatorWithEmptyValueList_IsRejected()
    {
        var r = await Check(WithCondition("<condition attribute=\"productnumber\" operator=\"in\"></condition>"));
        Assert.False(r.IsValid);
    }

    /// <summary>
    /// Verifies that in operator with value elements passes.
    /// </summary>
    [Fact]
    public async Task InOperatorWithValueElements_Passes()
    {
        var r = await Check(WithCondition("<condition attribute=\"productnumber\" operator=\"in\"><value>A</value><value>B</value></condition>"));
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Verifies that between with one value is rejected.
    /// </summary>
    [Fact]
    public async Task BetweenWithOneValue_IsRejected()
    {
        var r = await Check(WithCondition("<condition attribute=\"productnumber\" operator=\"between\"><value>A</value></condition>"));
        Assert.Contains(r.Errors, e => e.Contains("'between'") && e.Contains("exactly two"));
    }

    /// <summary>
    /// Verifies that eq without value is rejected.
    /// </summary>
    [Fact]
    public async Task EqWithoutValue_IsRejected()
    {
        var r = await Check(WithCondition("<condition attribute=\"name\" operator=\"eq\" />"));
        Assert.Contains(r.Errors, e => e.Contains("'eq'") && e.Contains("at least one"));
    }

    /// <summary>
    /// Verifies that operators that take no value pass without one.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("not-null")]
    [InlineData("today")]
    [InlineData("this-month")]
    public async Task OperatorsThatTakeNoValue_PassWithoutOne(string op)
    {
        var r = await Check(WithCondition($"<condition attribute=\"name\" operator=\"{op}\" />"));
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    private const string LinkQuery =
        "<fetch><entity name=\"contractdetail\"><attribute name=\"title\" />" +
        "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"contract\" link-type=\"inner\">" +
        "<attribute name=\"title\" /><filter><condition attribute=\"customerid\" operator=\"eq\" value=\"11111111-1111-1111-1111-111111111111\" /></filter>" +
        "</link-entity></entity></fetch>";

    /// <summary>
    /// Verifies that valid link entity passes.
    /// </summary>
    [Fact]
    public async Task ValidLinkEntity_Passes()
    {
        var r = await Check(LinkQuery);
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Verifies that link entity with bad to attribute fails.
    /// </summary>
    [Fact]
    public async Task LinkEntityWithBadToAttribute_Fails()
    {
        var r = await Check(LinkQuery.Replace("to=\"contractid\"", "to=\"nope\""));
        Assert.Contains(r.Errors, e => e.Contains("'nope'"));
    }

    /// <summary>
    /// Verifies that link entity without alias fails.
    /// </summary>
    [Fact]
    public async Task LinkEntityWithoutAlias_Fails()
    {
        var r = await Check(LinkQuery.Replace(" alias=\"contract\"", ""));
        Assert.Contains(r.Errors, e => e.Contains("alias"));
    }

    /// <summary>
    /// Verifies that condition directly under entity is rejected.
    /// </summary>
    [Fact]
    public async Task ConditionDirectlyUnderEntity_IsRejected()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><condition attribute=\"name\" operator=\"drop\" value=\"x\" /></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("<condition>") && e.Contains("<entity>"));
    }

    /// <summary>
    /// Verifies that condition directly under link entity is rejected.
    /// </summary>
    [Fact]
    public async Task ConditionDirectlyUnderLinkEntity_IsRejected()
    {
        var r = await Check(LinkQuery.Replace("<filter><condition", "<condition").Replace("</filter></link-entity>", "</link-entity>"));
        Assert.Contains(r.Errors, e => e.Contains("<condition>") && e.Contains("<link-entity>"));
    }

    /// <summary>
    /// Verifies that attribute inside filter is rejected.
    /// </summary>
    [Fact]
    public async Task AttributeInsideFilter_IsRejected()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><attribute name=\"name\" /></filter></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("<attribute>") && e.Contains("<filter>"));
    }

    /// <summary>
    /// Verifies that value outside condition is rejected.
    /// </summary>
    [Fact]
    public async Task ValueOutsideCondition_IsRejected()
    {
        var r = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /><value>x</value></entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("<value>") && e.Contains("<entity>"));
    }

    /// <summary>
    /// Verifies that filter on link alias before the link entity passes.
    /// </summary>
    [Fact]
    public async Task FilterOnLinkAlias_BeforeTheLinkEntity_Passes()
    {
        var r = await Check(
            "<fetch><entity name=\"contractdetail\"><attribute name=\"title\" />" +
            "<filter><condition entityname=\"contract\" attribute=\"title\" operator=\"eq\" value=\"x\" /></filter>" +
            "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"contract\" link-type=\"inner\">" +
            "<attribute name=\"title\" /></link-entity></entity></fetch>");
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Verifies that filter on unknown alias still fails.
    /// </summary>
    [Fact]
    public async Task FilterOnUnknownAlias_StillFails()
    {
        var r = await Check(
            "<fetch><entity name=\"contractdetail\"><attribute name=\"title\" />" +
            "<filter><condition entityname=\"ghost\" attribute=\"title\" operator=\"eq\" value=\"x\" /></filter>" +
            "</entity></fetch>");
        Assert.Contains(r.Errors, e => e.Contains("Unknown alias 'ghost'"));
    }

    /// <summary>
    /// Verifies that plan with two steps fails.
    /// </summary>
    [Fact]
    public async Task Plan_WithTwoSteps_Fails()
    {
        var step = new PlanStep("<fetch><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>");
        var r = await Validator().ValidateAsync(new QueryPlan(new List<PlanStep> { step, step }));
        Assert.Contains(r.Errors, e => e.Contains("exactly one step"));
    }

    /// <summary>
    /// Verifies that plan with run only if fails.
    /// </summary>
    [Fact]
    public async Task Plan_WithRunOnlyIf_Fails()
    {
        var step = new PlanStep("<fetch><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>", "previous step empty");
        var r = await Validator().ValidateAsync(new QueryPlan(new List<PlanStep> { step }));
        Assert.Contains(r.Errors, e => e.Contains("runOnlyIf"));
    }
}
