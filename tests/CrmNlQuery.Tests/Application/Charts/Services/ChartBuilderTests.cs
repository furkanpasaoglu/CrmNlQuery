using CrmNlQuery.Application.Charts.Services;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Tests.Application.Charts.Services;

/// <summary>
/// Tests chart building from aggregate results, including kinds, limits and reasons for no chart.
/// </summary>
public class ChartBuilderTests
{
    private static ResultTable Table(string[] columns, params string[][] raw)
        => new ResultTable(columns, raw.Select(r => (IReadOnlyList<string>)r).ToList()) { Raw = raw.Select(r => (IReadOnlyList<string>)r).ToList() };

    private static string Fetch(string attributes, string order = "")
        => "<fetch aggregate=\"true\"><entity name=\"opportunity\">" + attributes + order + "</entity></fetch>";

    private const string SumValue = "<attribute name=\"estimatedvalue\" aggregate=\"sum\" alias=\"total\" />";
    private const string Year = "<attribute name=\"createdon\" groupby=\"true\" dategrouping=\"year\" alias=\"y\" />";
    private const string Month = "<attribute name=\"createdon\" groupby=\"true\" dategrouping=\"month\" alias=\"m\" />";
    private const string Priority = "<attribute name=\"prioritycode\" groupby=\"true\" alias=\"p\" />";
    private const string Count = "<attribute name=\"incidentid\" aggregate=\"count\" alias=\"n\" />";

    /// <summary>
    /// Verifies that not an aggregate has no chart.
    /// </summary>
    [Fact]
    public void NotAnAggregate_HasNoChart()
    {
        var table = Table(new[] { "name" }, new[] { "a" }, new[] { "b" });

        Assert.Null(ChartBuilder.Build("<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", table, false));
    }

    /// <summary>
    /// Verifies that why not a list is not a chart candidate.
    /// </summary>
    [Fact]
    public void WhyNot_AListIsNotAChartCandidate()
    {
        var table = Table(new[] { "name" }, new[] { "a" }, new[] { "b" });
        Assert.Equal("list", ChartBuilder.WhyNot("<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", table));
        Assert.Equal("list", ChartBuilder.WhyNot(Fetch(Count), Table(new[] { "n" }, new[] { "3" })));
    }

    /// <summary>
    /// Verifies that why not one group row is too few for a chart.
    /// </summary>
    [Fact]
    public void WhyNot_OneGroupRowIsTooFewForAChart()
    {
        var table = Table(new[] { "p", "n" }, new[] { "Active", "1" });
        Assert.Equal("few", ChartBuilder.WhyNot(Fetch(Priority + Count), table));
        Assert.Null(ChartBuilder.Build(Fetch(Priority + Count), table, false));
    }

    /// <summary>
    /// Verifies that why not two measures are unsupported.
    /// </summary>
    [Fact]
    public void WhyNot_TwoMeasures_AreUnsupported()
    {
        var fetch = Fetch(Priority + Count + SumValue);
        var table = Table(new[] { "p", "n", "total" }, new[] { "High", "1", "5" }, new[] { "Low", "2", "6" });
        Assert.Equal("unsupported", ChartBuilder.WhyNot(fetch, table));
    }

    /// <summary>
    /// Verifies that a category aggregate is a bar chart sorted by value descending.
    /// </summary>
    [Fact]
    public void ACategoryAggregate_IsABarChart_SortedByValueDescending()
    {
        var table = Table(new[] { "p", "n" }, new[] { "Low", "2" }, new[] { "High", "9" }, new[] { "Normal", "5" });

        var chart = ChartBuilder.Build(Fetch(Priority + Count), table, false)!;

        Assert.Equal("bar", chart.Kind);
        Assert.Equal(new[] { "High", "Normal", "Low" }, chart.XLabels);
        Assert.Equal(new double?[] { 9, 5, 2 }, chart.Series[0].Values);
        Assert.Equal("count", chart.Fn);
    }

    /// <summary>
    /// Verifies that long category labels use horizontal bars.
    /// </summary>
    [Fact]
    public void LongCategoryLabels_UseHorizontalBars()
    {
        var fetch = Fetch("<attribute name=\"customerid\" groupby=\"true\" alias=\"c\" />" + SumValue);
        var table = Table(new[] { "c", "total" }, new[] { "A very long company name Ltd", "10" }, new[] { "Another very long company name", "5" });

        Assert.Equal("hbar", ChartBuilder.Build(fetch, table, false)!.Kind);
    }

    /// <summary>
    /// Verifies that more than ten categories keep the largest ten and add other for a sum.
    /// </summary>
    [Fact]
    public void MoreThanTenCategories_KeepTheLargestTen_AndAddOther_ForASum()
    {
        var fetch = Fetch("<attribute name=\"customerid\" groupby=\"true\" alias=\"c\" />" + SumValue);
        var rows = Enumerable.Range(1, 13).Select(i => new[] { "C" + i, i.ToString() }).ToArray();

        var chart = ChartBuilder.Build(fetch, Table(new[] { "c", "total" }, rows), false)!;

        Assert.Equal(11, chart.XLabels.Count);
        Assert.True(chart.HasOther);
        Assert.Equal("Other", chart.XLabels[10]);
        Assert.Equal(1 + 2 + 3, chart.Series[0].Values[10]);
        Assert.Equal(13, chart.Series[0].Values[0]);
    }

    /// <summary>
    /// Verifies that more than ten categories for an average have no other but say how many were left out.
    /// </summary>
    [Fact]
    public void MoreThanTenCategories_ForAnAverage_HaveNoOther_ButSayHowManyWereLeftOut()
    {
        var fetch = Fetch("<attribute name=\"customerid\" groupby=\"true\" alias=\"c\" /><attribute name=\"estimatedvalue\" aggregate=\"avg\" alias=\"mean\" />");
        var rows = Enumerable.Range(1, 12).Select(i => new[] { "C" + i, i.ToString() }).ToArray();

        var chart = ChartBuilder.Build(fetch, Table(new[] { "c", "mean" }, rows), false)!;

        Assert.Equal(10, chart.XLabels.Count);
        Assert.False(chart.HasOther);
        Assert.Equal(2, chart.OmittedCategories);
    }

    /// <summary>
    /// Verifies that year and month without repeated months is one chronological line with empty months as zero.
    /// </summary>
    [Fact]
    public void YearAndMonth_WithoutRepeatedMonths_IsOneChronologicalLine_WithEmptyMonthsAsZero()
    {
        var table = Table(new[] { "y", "m", "total" }, new[] { "2026", "2", "30" }, new[] { "2025", "11", "10" }, new[] { "2025", "12", "20" });

        var chart = ChartBuilder.Build(Fetch(Year + Month + SumValue), table, false)!;

        Assert.Equal("line", chart.Kind);
        Assert.Equal("yearmonth", chart.XKind);
        Assert.Equal(new[] { "2025-11", "2025-12", "2026-01", "2026-02" }, chart.XLabels);
        Assert.Equal(new double?[] { 10, 20, 0, 30 }, chart.Series[0].Values);
    }

    /// <summary>
    /// Verifies that year and month with repeated months is one line per year.
    /// </summary>
    [Fact]
    public void YearAndMonth_WithRepeatedMonths_IsOneLinePerYear()
    {
        var table = Table(new[] { "y", "m", "total" },
            new[] { "2025", "1", "5" }, new[] { "2025", "2", "6" }, new[] { "2026", "1", "7" }, new[] { "2026", "2", "8" });

        var chart = ChartBuilder.Build(Fetch(Year + Month + SumValue), table, false)!;

        Assert.Equal("month", chart.XKind);
        Assert.Equal(new[] { "1", "2" }, chart.XLabels);
        Assert.Equal(new[] { "2025", "2026" }, chart.Series.Select(s => s.Name));
        Assert.Equal(new double?[] { 7, 8 }, chart.Series[1].Values);
    }

    /// <summary>
    /// Verifies that years alone are a line with gap years as zero.
    /// </summary>
    [Fact]
    public void YearsAlone_AreALine_WithGapYearsAsZero()
    {
        var table = Table(new[] { "y", "n" }, new[] { "2024", "3" }, new[] { "2026", "4" });

        var chart = ChartBuilder.Build(Fetch(Year + Count), table, false)!;

        Assert.Equal("line", chart.Kind);
        Assert.Equal(new[] { "2024", "2025", "2026" }, chart.XLabels);
        Assert.Equal(new double?[] { 3, 0, 4 }, chart.Series[0].Values);
    }

    /// <summary>
    /// Verifies that two category groupings are not charted.
    /// </summary>
    [Fact]
    public void TwoCategoryGroupings_AreNotCharted()
    {
        var second = "<attribute name=\"statecode\" groupby=\"true\" alias=\"s\" />";
        var table = Table(new[] { "p", "s", "n" }, new[] { "High", "Active", "1" }, new[] { "Low", "Active", "2" });

        Assert.Null(ChartBuilder.Build(Fetch(Priority + second + Count), table, false));
    }

    /// <summary>
    /// Verifies that a single point is not charted.
    /// </summary>
    [Fact]
    public void ASinglePoint_IsNotCharted()
    {
        Assert.Null(ChartBuilder.Build(Fetch(Priority + Count), Table(new[] { "p", "n" }, new[] { "High", "1" }), false));
    }

    /// <summary>
    /// Verifies that the hidden row ID column is ignored.
    /// </summary>
    [Fact]
    public void TheHiddenRowIdColumn_IsIgnored()
    {
        var rowId = "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" />";
        var table = Table(new[] { "p", "n" }, new[] { "High", "4" }, new[] { "Low", "2" });

        Assert.NotNull(ChartBuilder.Build(Fetch(rowId + Priority + Count), table, false));
    }

    /// <summary>
    /// Verifies that a truncated result is marked partial.
    /// </summary>
    [Fact]
    public void ATruncatedResult_IsMarkedPartial()
    {
        var chart = ChartBuilder.Build(Fetch(Priority + Count), Table(new[] { "p", "n" }, new[] { "High", "4" }, new[] { "Low", "2" }), true)!;

        Assert.True(chart.Partial);
    }

    /// <summary>
    /// Verifies that no raw values means no chart.
    /// </summary>
    [Fact]
    public void NoRawValues_MeansNoChart()
    {
        var table = new ResultTable(new[] { "p", "n" }, new List<IReadOnlyList<string>> { new[] { "High", "4" }, new[] { "Low", "2" } });

        Assert.Null(ChartBuilder.Build(Fetch(Priority + Count), table, false));
    }
}
