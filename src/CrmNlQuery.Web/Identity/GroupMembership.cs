using System.Security.Claims;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Web.Identity;

/// <summary>
/// Answers whether a user belongs to a given AD group, using the verified Windows token or the fixed groups in development, following the caller-identity rules.
/// </summary>
public static class GroupMembership
{
    /// <summary>
    /// Creates a function that tests membership in a named AD group for the given options and Windows principal.
    /// </summary>
    public static Func<string, bool> Create(IdentityOptions options, ClaimsPrincipal? user)
    {
        var mode = (options.Mode ?? "").Trim();
        if (mode.Equals("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            var fixedGroups = new HashSet<string>(
                (options.FixedGroups ?? new string[0]).Select(g => (g ?? "").Trim()).Where(g => g.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            return group => fixedGroups.Contains((group ?? "").Trim());
        }
        if ((mode.Equals("Windows", StringComparison.OrdinalIgnoreCase) || mode.Equals("CrmProof", StringComparison.OrdinalIgnoreCase)) && user is not null)
        {
            return group =>
            {
                try
                {
                    return user.IsInRole(group);
                }
                catch (Exception)
                {
                    return false;
                }
            };
        }
        return _ => false;
    }
}
