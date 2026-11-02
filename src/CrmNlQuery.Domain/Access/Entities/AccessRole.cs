namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// Access role with id, name, built-in flag and permissions; the built-in Administrator holds every permission and its list is not stored.
/// </summary>
public sealed record AccessRole(Guid Id, string Name, bool Builtin, IReadOnlyList<string> Permissions);
