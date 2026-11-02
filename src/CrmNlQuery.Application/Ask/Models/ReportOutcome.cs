namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Result type returned by the ask pipeline for report outcomes.
/// </summary>
public sealed record ReportOutcome(string Query, IReadOnlyList<CrmNlQuery.Application.Reports.Models.ReportItem> Reports, bool ListedAll) : AskOutcome
{
    public string? ResultId { get; init; }
    public string? ChatId { get; init; }
}
