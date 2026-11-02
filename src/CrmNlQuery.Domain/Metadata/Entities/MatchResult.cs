namespace CrmNlQuery.Domain.Metadata.Entities;

/// <summary>
/// Carries the result of matching a token to an attribute: the match and any candidates.
/// </summary>
public sealed record MatchResult(AttributeInfo? Attribute, IReadOnlyList<AttributeInfo> Suggestions);
