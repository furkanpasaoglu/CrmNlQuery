namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// Panel user record: folded account key, display name as typed, active flag, creation info and assigned role ids.
/// </summary>
public sealed record AccessUser(string Account, string DisplayName, bool Active, DateTimeOffset CreatedAt, string CreatedBy, IReadOnlyList<Guid> RoleIds);
