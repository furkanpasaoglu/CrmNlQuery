namespace CrmNlQuery.Domain.Quality.Entities;

/// <summary>
/// An acceptance-set case with a question, a reference query and compare rules.
/// </summary>
public sealed record QualityCase
{
    public const string SourceMarked = "marked";
    public const string SourceGenerated = "generated";
    public const string SourceSuggested = "suggested";
    public const string StatusActive = "active";
    public const string StatusRetired = "retired";
    public const string StatusPending = "pending";

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string Source { get; init; } = SourceGenerated;
    public string Status { get; init; } = StatusActive;
    public bool Verified { get; init; }
    public string Language { get; init; } = "en";
    public string Question { get; init; } = "";
    public string Entity { get; init; } = "";
    public string? AnswersJson { get; init; }
    public string ReferenceFetch { get; init; } = "";
    public string? CompareJson { get; init; }
    public string? Template { get; init; }
    public string? Note { get; init; }
}
