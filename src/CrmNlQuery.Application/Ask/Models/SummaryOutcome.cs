namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Result type returned by the ask pipeline for summary outcomes.
/// </summary>
public sealed record SummaryOutcome(string Entity, string EntityLabel, string RecordId, string RecordName, IReadOnlyList<CrmNlQuery.Application.Summary.Models.SummarySection> Sections) : AskOutcome
{
    public string? ResultId { get; init; }
    public string? ChatId { get; init; }
    public string? RecordUrlBase { get; init; }
    public string? AboutRecord { get; init; }
}
