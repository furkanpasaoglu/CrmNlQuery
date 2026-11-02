namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Represents a mention resolved in the CRM to a specific record id, entity and display name.
/// </summary>
public sealed record ResolvedRecord(string Mention, string Entity, Guid Id, string Name);
