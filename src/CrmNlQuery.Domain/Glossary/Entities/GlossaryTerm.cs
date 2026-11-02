namespace CrmNlQuery.Domain.Glossary.Entities;

/// <summary>
/// A glossary term text with its language, used for listing and eval only.
/// </summary>
public sealed record GlossaryTerm(string Text, string Language = "any");
