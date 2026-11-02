namespace CrmNlQuery.Application.Summary.Models;

/// <summary>
/// One section of a customer summary with its table, metrics and items.
/// </summary>
public sealed record SummarySection(string Kind, string Entity, string Title, string Status, IReadOnlyList<SummaryMetric> Metrics, IReadOnlyList<SummaryItem> Items)
{
    public string? Via { get; init; }
    public string? ViaEntity { get; init; }
    public bool CanOpen { get; init; }

    /// <summary>
    /// Creates a summary section marked as unavailable with a reason.
    /// </summary>
    public static SummarySection Unavailable(string kind, string entity, string title)
        => new SummarySection(kind, entity, title, SummaryStatus.Unavailable, Array.Empty<SummaryMetric>(), Array.Empty<SummaryItem>());
}
