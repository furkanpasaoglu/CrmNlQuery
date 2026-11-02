namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Result type returned by the ask pipeline for draft outcomes.
/// </summary>
public sealed record DraftOutcome(string Entity, string RecordId, string RecordName, string Subject, string Body, string? To, string Tone, IReadOnlyList<string> Unresolved, string TemplateSubject, string TemplateBody) : AskOutcome
{
    public string? ResultId { get; init; }
    public string? ChatId { get; init; }
    public string? AboutRecord { get; init; }
}
