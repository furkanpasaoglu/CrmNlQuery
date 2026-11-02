namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Represents a record or company name mentioned in a question with its entity.
/// </summary>
public sealed record Mention(string Text, string Entity);
