using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Explain.Services;

/// <summary>
/// Tests that relative date operators are turned into real ranges with a fixed clock; unknown operators never invent a date.
/// </summary>
public class QueryExplainerDateTests
{
    private static readonly EntityInfo Opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo> { new AttributeInfo("createdon", "Created On", "DateTime", new List<OptionInfo>()) },
        new List<RelationshipInfo>());

    private static Task<QueryExplanation?> Explain(string op, string? value)
    {
        var explainer = new QueryExplainer(new FakeMetadataSource(Opportunity), () => new DateTimeOffset(2026, 10, 22, 15, 30, 0, TimeSpan.Zero));
        var valueAttr = value is null ? "" : $" value=\"{value}\"";
        return explainer.ExplainAsync($"<fetch><entity name=\"opportunity\"><filter><condition attribute=\"createdon\" operator=\"{op}\"{valueAttr} /></filter></entity></fetch>");
    }

    /// <summary>
    /// Verifies that known relative date operators are converted to the expected real date ranges.
    /// </summary>
    [Theory]
    [InlineData("today", null, "2026-10-22", "2026-10-22")]
    [InlineData("yesterday", null, "2026-10-21", "2026-10-21")]
    [InlineData("tomorrow", null, "2026-10-23", "2026-10-23")]
    [InlineData("this-week", null, "2026-10-18", "2026-10-24")]
    [InlineData("last-week", null, "2026-10-11", "2026-10-17")]
    [InlineData("next-week", null, "2026-10-25", "2026-10-31")]
    [InlineData("this-month", null, "2026-10-01", "2026-10-31")]
    [InlineData("last-month", null, "2026-09-01", "2026-09-30")]
    [InlineData("next-month", null, "2026-11-01", "2026-11-30")]
    [InlineData("this-year", null, "2026-01-01", "2026-12-31")]
    [InlineData("last-year", null, "2025-01-01", "2025-12-31")]
    [InlineData("next-year", null, "2027-01-01", "2027-12-31")]
    [InlineData("last-x-days", "30", "2026-09-22", "2026-10-22")]
    [InlineData("next-x-days", "7", "2026-10-22", "2026-10-29")]
    [InlineData("last-x-weeks", "2", "2026-10-08", "2026-10-22")]
    [InlineData("next-x-weeks", "2", "2026-10-22", "2026-11-05")]
    [InlineData("last-x-months", "3", "2026-07-22", "2026-10-22")]
    [InlineData("next-x-months", "1", "2026-10-22", "2026-11-22")]
    [InlineData("last-x-years", "1", "2025-10-22", "2026-10-22")]
    [InlineData("next-x-years", "1", "2026-10-22", "2027-10-22")]
    [InlineData("last-seven-days", null, "2026-10-15", "2026-10-22")]
    [InlineData("on", "2026-03-05T10:00:00", "2026-03-05", "2026-03-05")]
    public async Task RelativeAndExactDateOperators_ResolveToARealRange(string op, string? value, string from, string to)
    {
        var f = Assert.Single((await Explain(op, value))!.Filters);

        Assert.Equal(from, f.From);
        Assert.Equal(to, f.To);
    }

    /// <summary>
    /// Verifies that older-than operators convert to the expected boundary dates.
    /// </summary>
    [Theory]
    [InlineData("olderthan-x-months", "6", null, "2026-04-22")]
    [InlineData("olderthan-x-days", "10", null, "2026-10-12")]
    [InlineData("olderthan-x-years", "1", null, "2025-10-22")]
    [InlineData("on-or-after", "2026-03-05", "2026-03-05", null)]
    [InlineData("on-or-before", "2026-03-05", null, "2026-03-05")]
    public async Task OpenEndedOperators_ResolveOneSideOnly(string op, string value, string? from, string? to)
    {
        var f = Assert.Single((await Explain(op, value))!.Filters);

        Assert.Equal(from, f.From);
        Assert.Equal(to, f.To);
    }

    /// <summary>
    /// Verifies that out-of-range or absurd relative values produce no range.
    /// </summary>
    [Theory]
    [InlineData("next-x-years", "10000")]
    [InlineData("olderthan-x-years", "10000")]
    [InlineData("last-x-years", "10000")]
    public async Task AnAbsurdlyLargeNumber_ResolvesNoRange_ButTheRestOfTheExplanationSurvives(string op, string value)
    {
        var e = await Explain(op, value);

        var f = Assert.Single(e!.Filters);
        Assert.Null(f.From);
        Assert.Null(f.To);
    }

    /// <summary>
    /// Verifies that unrecognized operators or invalid values produce no invented date.
    /// </summary>
    [Theory]
    [InlineData("foo-bar", null)]
    [InlineData("last-x-days", "not-a-number")]
    [InlineData("eq", "2026-03-05")]
    public async Task AnUnknownOrUnparsableOperator_ResolvesNoRange_AndInventsNoDate(string op, string? value)
    {
        var f = Assert.Single((await Explain(op, value))!.Filters);

        Assert.Null(f.From);
        Assert.Null(f.To);
        Assert.Equal(op, f.Op);
    }
}
