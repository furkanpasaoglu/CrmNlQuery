using CrmNlQuery.Application.Ask.Models;

namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Holds the result of a record creation request: ask, preview, created or failed, with entity, draft id and rows.
/// </summary>
public sealed record CreateOutcome(string Kind, string Entity, string EntityLabel, string? DraftId, IReadOnlyList<CreateRowView> Rows) : AskOutcome
{
    public string? ResultId { get; init; }

    public string? ChatId { get; init; }

    public string? RecordUrlBase { get; init; }

    public string? Question { get; init; }
}
