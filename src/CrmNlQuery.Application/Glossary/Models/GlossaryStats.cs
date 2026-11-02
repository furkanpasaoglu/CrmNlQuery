namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Per-entry counters of how often a glossary entry was applied or not.
/// </summary>
public sealed record GlossaryStats(string EntryId, int Applied, int NotApplied, DateTimeOffset? LastNotAppliedAt);
