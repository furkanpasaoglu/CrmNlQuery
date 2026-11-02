using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Web.Identity;

/// <summary>
/// Reads the group names from the signed-in user's own token for diagnostics and the group picker.
/// </summary>
public static class TokenGroups
{
    public const int AuditLimit = 40;
    public const int ScreenLimit = 200;

    /// <summary>
    /// Returns the user's token group names in a stable order, limited in count, without leaking errors.
    /// </summary>
    public static IReadOnlyList<string> Names(IdentityOptions options, ClaimsPrincipal? user, int max = ScreenLimit)
    {
        try
        {
            var mode = (options.Mode ?? "").Trim();
            if (mode.Equals("Fixed", StringComparison.OrdinalIgnoreCase))
            {
                return (options.FixedGroups ?? new string[0]).Select(g => (g ?? "").Trim()).Where(g => g.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            }
            if (mode.Equals("CrmProof", StringComparison.OrdinalIgnoreCase) && user is not null)
            {
                return user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            }
            if (mode.Equals("Windows", StringComparison.OrdinalIgnoreCase) && user is not null && OperatingSystem.IsWindows())
            {
                return WindowsNames(user, max);
            }
        }
        catch (Exception)
        {
        }
        return new List<string>();
    }

    /// <summary>
    /// Returns the group names as a comma-separated text limited in count, or null when there are none.
    /// </summary>
    public static string? Describe(IdentityOptions options, ClaimsPrincipal? user)
    {
        var names = Names(options, user, AuditLimit);
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> WindowsNames(ClaimsPrincipal user, int max)
    {
        var sids = new List<SecurityIdentifier>();
        var windows = user.Identities.OfType<WindowsIdentity>().FirstOrDefault();
        if (windows?.Groups is { } groups)
        {
            sids.AddRange(groups.OfType<SecurityIdentifier>());
        }
        else
        {
            foreach (var claim in user.FindAll(ClaimTypes.GroupSid))
            {
                try { sids.Add(new SecurityIdentifier(claim.Value)); } catch (ArgumentException) { }
            }
        }

        var ordered = sids.Select(s => s.Value).Distinct(StringComparer.Ordinal)
            .OrderBy(v => v.StartsWith("S-1-5-21-", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(v => v, StringComparer.Ordinal)
            .Take(max)
            .ToList();

        var names = new List<string>();
        foreach (var value in ordered)
        {
            try { names.Add(new SecurityIdentifier(value).Translate(typeof(NTAccount)).Value); }
            catch (Exception) { names.Add(value); }
        }
        return names.OrderBy(n => n.StartsWith("S-1-", StringComparison.Ordinal) ? 1 : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
