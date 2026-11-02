namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// Mapping from an AD group (folded key plus display name used for Windows membership checks) to the roles it grants.
/// </summary>
public sealed record GroupMapping(string Group, string DisplayName, IReadOnlyList<Guid> RoleIds);
