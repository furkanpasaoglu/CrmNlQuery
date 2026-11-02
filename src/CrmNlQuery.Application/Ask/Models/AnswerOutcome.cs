using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Result type returned by the ask pipeline for answer outcomes.
/// </summary>
public sealed record AnswerOutcome(string Entity, string Understood, string FetchXml, ResultTable Table, bool Truncated) : AskOutcome
{
    public IReadOnlyList<string> StepNotes { get; init; } = Array.Empty<string>();
    public string? ResultId { get; init; }
    public string? BasedOn { get; init; }
    public string? AboutRecord { get; init; }
    public string? ChatId { get; init; }
    public bool OpenInCrm { get; init; }
    public string? RecordUrlBase { get; init; }
    public IReadOnlyList<NarrowHint> NarrowHints { get; init; } = Array.Empty<NarrowHint>();
    public IReadOnlyList<StepSummary> StepSummaries { get; init; } = Array.Empty<StepSummary>();
    public bool FromChain { get; init; }
    public QueryExplanation? Explanation { get; init; }
    public IReadOnlyList<ResolvedRecord> ResolvedRecords { get; init; } = Array.Empty<ResolvedRecord>();
    public bool ShowFetchXml { get; init; }
    public CrmNlQuery.Application.Charts.Models.ChartData? Chart { get; init; }

    public IReadOnlyList<string>? ColumnLabels { get; init; }

    public string? ChartReason { get; init; }
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = Array.Empty<Suggestion>();
    public IReadOnlyList<GlossaryApplied> GlossaryApplied { get; init; } = Array.Empty<GlossaryApplied>();
    public int? DurationMs { get; init; }
}
