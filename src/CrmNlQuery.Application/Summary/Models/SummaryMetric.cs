namespace CrmNlQuery.Application.Summary.Models;

/// <summary>
/// One summary metric with a stable machine key, label and value.
/// </summary>
public sealed record SummaryMetric(string Key, string Label, string Value);
