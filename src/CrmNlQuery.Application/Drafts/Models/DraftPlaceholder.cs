namespace CrmNlQuery.Application.Drafts.Models;

/// <summary>
/// Defines one email draft placeholder with its name, display label and meaning.
/// </summary>
public sealed record DraftPlaceholder(string Name, string Label, string Meaning);
