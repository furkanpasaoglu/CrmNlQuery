using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests date grouping validation: only in aggregate queries, with groupby, on a date field and with a known unit.
/// </summary>
public class PlanValidatorDateGroupingTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static Task<ValidationResult> Check(string xml)
    {
        var opp = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A("estimatedvalue", "Money"), A("actualclosedate", "DateTime") },
            new List<RelationshipInfo>());
        return new PlanValidator(new FakeMetadataSource(opp)).ValidateFetchXmlAsync(xml);
    }

    private const string Monthly =
        "<fetch aggregate=\"true\"><entity name=\"opportunity\">" +
        "<attribute name=\"actualclosedate\" groupby=\"true\" dategrouping=\"year\" alias=\"y\" />" +
        "<attribute name=\"actualclosedate\" groupby=\"true\" dategrouping=\"month\" alias=\"m\" />" +
        "<attribute name=\"estimatedvalue\" aggregate=\"sum\" alias=\"total\" />" +
        "<order alias=\"y\" /><order alias=\"m\" /></entity></fetch>";

    /// <summary>
    /// Verifies that year and month grouping on a date field passes.
    /// </summary>
    [Fact]
    public async Task YearAndMonthGrouping_OnADateField_Passes()
    {
        var result = await Check(Monthly);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that each supported date grouping unit passes.
    /// </summary>
    [Theory]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    [InlineData("quarter")]
    [InlineData("year")]
    public async Task EveryKnownUnit_Passes(string unit)
    {
        var result = await Check($"<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"actualclosedate\" groupby=\"true\" dategrouping=\"{unit}\" alias=\"g\" /><attribute name=\"estimatedvalue\" aggregate=\"sum\" alias=\"t\" /></entity></fetch>");

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that an unknown unit is rejected.
    /// </summary>
    [Fact]
    public async Task UnknownUnit_IsRejected()
    {
        var result = await Check(Monthly.Replace("\"month\"", "\"fortnight\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("dategrouping"));
    }

    /// <summary>
    /// Verifies that a non-date field is rejected.
    /// </summary>
    [Fact]
    public async Task NonDateField_IsRejected()
    {
        var result = await Check(Monthly.Replace("name=\"actualclosedate\" groupby=\"true\" dategrouping=\"month\"", "name=\"name\" groupby=\"true\" dategrouping=\"month\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("date"));
    }

    /// <summary>
    /// Verifies that date grouping without groupby is rejected.
    /// </summary>
    [Fact]
    public async Task WithoutGroupBy_IsRejected()
    {
        var result = await Check(Monthly.Replace("groupby=\"true\" dategrouping=\"month\"", "dategrouping=\"month\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("groupby"));
    }

    /// <summary>
    /// Verifies that date grouping outside an aggregate query is rejected.
    /// </summary>
    [Fact]
    public async Task OutsideAnAggregateQuery_IsRejected()
    {
        var result = await Check("<fetch><entity name=\"opportunity\"><attribute name=\"actualclosedate\" dategrouping=\"month\" /></entity></fetch>");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("aggregate"));
    }
}
