namespace CrmNlQuery.Application.Charts.Models;

/// <summary>
/// Holds one chart series with its name and values.
/// </summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double?> Values);
