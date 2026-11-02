namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// A suggestion for narrowing a truncated result, computed from field and option metadata rather than row data.
/// </summary>
public sealed record NarrowHint(string Attribute, string Label, string Kind, IReadOnlyList<string> Options);
