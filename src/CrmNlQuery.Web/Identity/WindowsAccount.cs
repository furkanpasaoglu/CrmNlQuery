using System.Security.Claims;

namespace CrmNlQuery.Web.Identity;

/// <summary>
/// Extracts the Windows account name from the authenticated request principal.
/// </summary>
public static class WindowsAccount
{
    /// <summary>
    /// Returns the DOMAIN\user account if IIS Windows authentication verified the user, or null if anonymous or unverified.
    /// </summary>
    public static string? From(ClaimsPrincipal? user)
    {
        var identity = user?.Identity;
        if (identity is null || !identity.IsAuthenticated)
        {
            return null;
        }
        var name = identity.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
