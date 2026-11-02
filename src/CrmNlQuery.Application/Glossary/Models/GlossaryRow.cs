using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Represents one row of the glossary list: the entry, its status, a reason code if broken, and usage counters.
/// </summary>
public sealed record GlossaryRow(GlossaryEntry Entry, string Status, string? Reason, int Applied, int NotApplied, DateTimeOffset? LastNotAppliedAt);
