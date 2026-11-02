namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Dashboard summary of glossary entries containing only counts, no terms or content.
/// </summary>
public sealed record GlossarySummary(bool Enabled, int Entries, int Broken, int NotApplied);
