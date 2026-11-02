namespace CrmNlQuery.Domain.Glossary.Entities;

/// <summary>
/// A glossary entry matched by a question together with the matched phrase.
/// </summary>
public sealed record GlossaryHit(GlossaryEntry Entry, string Phrase);
