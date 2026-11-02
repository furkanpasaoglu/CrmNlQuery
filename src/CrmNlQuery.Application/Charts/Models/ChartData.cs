namespace CrmNlQuery.Application.Charts.Models;

/// <summary>
/// Holds chart data prepared by the server as numbers and labels, with formatting left to the browser.
/// </summary>
public sealed record ChartData(string Kind, string XKind, string MeasureName, string Fn, IReadOnlyList<string> XLabels, IReadOnlyList<ChartSeries> Series)
{
    public bool HasOther { get; init; }

    public int OmittedCategories { get; init; }

    public bool Partial { get; init; }
}
