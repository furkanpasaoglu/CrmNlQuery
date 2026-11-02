using CrmNlQuery.Composition;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;

namespace CrmNlQuery.Web.Security;

/// <summary>
/// Checks widget requests against admin permissions by looking at the Windows account's panel roles, without auditing denials.
/// </summary>
public static class WidgetPermissions
{
    /// <summary>
    /// Returns the Windows account of the request, or null if unknown.
    /// </summary>
    public static string? Account(HttpContext http, AppServices services) => services.Identity.GetAccount(WindowsAccount.From(http.User));

    /// <summary>
    /// Checks whether the requesting account holds the given admin permission.
    /// </summary>
    public static async Task<bool> HasAsync(HttpContext http, AppServices services, string permission, CancellationToken ct)
    {
        var account = Account(http, services);
        if (account is null) { return false; }
        var decision = await new AdminAccess(services.Access).AuthorizeAsync(account, permission, ct, GroupMembership.Create(services.IdentityOptions, http.User));
        return decision.Decision == AdminDecision.Allowed;
    }
}
