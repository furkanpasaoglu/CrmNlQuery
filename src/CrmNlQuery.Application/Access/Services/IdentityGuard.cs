using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Guards against the Fixed identity mode, meant for the lab, being left on in production.
/// </summary>
public static class IdentityGuard
{
    /// <summary>
    /// Validates the identity options for the environment and refuses to start if Fixed is unsafe.
    /// </summary>
    public static void Validate(IdentityOptions options, string environmentName)
    {
        var isFixed = string.Equals((options.Mode ?? "").Trim(), "Fixed", StringComparison.OrdinalIgnoreCase);
        if (!isFixed)
        {
            return;
        }
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (!isDevelopment && !options.AllowFixedInProduction)
        {
            throw new InvalidOperationException(
                "Identity:Mode is Fixed, which makes EVERYONE act as one configured user. That is for lab work only and is refused outside " +
                "the Development environment. Use Identity:Mode=Windows behind IIS, or (lab servers only) set Identity:AllowFixedInProduction=true.");
        }
    }
}
