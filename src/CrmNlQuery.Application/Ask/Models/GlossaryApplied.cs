namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Record of a glossary entry applied to a question (term, kind and explanatory text) shown to the user in the explanation.
/// </summary>
public sealed record GlossaryApplied(string Term, string Kind, string Text);
