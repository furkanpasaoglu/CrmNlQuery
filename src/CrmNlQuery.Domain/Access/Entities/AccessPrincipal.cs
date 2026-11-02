namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// A verified, registered and active panel user whose permissions the admin endpoints check.
/// </summary>
public sealed class AccessPrincipal
{
    private readonly HashSet<string> _permissions;

    public AccessPrincipal(string account, string displayName, IReadOnlyList<string> roleNames, bool isAdministrator, IEnumerable<string> permissions)
    {
        Account = account;
        DisplayName = displayName;
        RoleNames = roleNames;
        IsAdministrator = isAdministrator;
        _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
    }

    public string Account { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> RoleNames { get; }
    public bool IsAdministrator { get; }
    public IReadOnlyCollection<string> Permissions => _permissions;

    /// <summary>
    /// Checks whether it has for access principal.
    /// </summary>
    public bool Has(string permission) => IsAdministrator || _permissions.Contains(permission);
}
