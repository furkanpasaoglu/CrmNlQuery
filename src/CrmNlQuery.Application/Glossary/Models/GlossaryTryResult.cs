namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Result of trying a glossary entry, carrying an outcome and only counts, never row content.
/// </summary>
public sealed record GlossaryTryResult(string Outcome, int Rows = 0, bool Capped = false, string? Reason = null, string? Text = null);
