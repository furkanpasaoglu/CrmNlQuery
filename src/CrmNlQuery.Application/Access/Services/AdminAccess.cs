using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Authorizes admin panel access only for registered, active, permitted users of the tool's own access tables, independent of the CRM.
/// </summary>
public sealed class AdminAccess
{
    public const string NoAccess = "no_access";
    public const string MissingPermission = "missing_permission";

    private readonly AccessService _access;

    public AdminAccess(AccessService access)
    {
        _access = access;
    }

    /// <summary>
    /// Authorizes an account for a permission; null means registered and active is enough, and comma-separated permissions mean any of them.
    /// </summary>
    public async Task<AdminAuthorization> AuthorizeAsync(string account, string? permission, CancellationToken ct = default, Func<string, bool>? isMember = null)
    {
        try
        {
            var principal = await _access.ResolveAsync(account, ct, isMember);
            if (principal is null)
            {
                return new AdminAuthorization(AdminDecision.NotFound, null, NoAccess);
            }
            if (permission is not null && !permission.Split(',').Any(principal.Has))
            {
                return new AdminAuthorization(AdminDecision.NotFound, null, MissingPermission);
            }
            return new AdminAuthorization(AdminDecision.Allowed, principal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AdminAuthorization(AdminDecision.Unavailable, null);
        }
    }
}
