using System.Security.Claims;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Composition;

namespace CrmNlQuery.Web.Security;

/// <summary>
/// Sets the request user from a valid signed session token, so the rest of the service sees the CRM-proof user like a Windows user.
/// </summary>
public static class BearerAuthentication
{
    public const string AuthenticationType = "NlqCrmProof";
    public const string ExpiryClaim = "nlq:exp";

    /// <summary>
    /// Adds the middleware that reads the Authorization Bearer token on API requests; an invalid or expired token leaves the request anonymous.
    /// </summary>
    public static IApplicationBuilder UseBearerAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var header = context.Request.Headers.Authorization.ToString();
                if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = context.RequestServices.GetService<AppServices>()?.AuthTokens;
                    var claims = tokens?.Validate(header.Substring(7).Trim());
                    if (claims is not null)
                    {
                        context.User = CreatePrincipal(claims);
                    }
                }
            }
            await next();
        });
    }

    /// <summary>
    /// Builds the principal for a validated token: the account as name, the groups as roles, the CRM user id and the session end.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(AuthTokenClaims claims)
    {
        var identity = new ClaimsIdentity(AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, claims.Account));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, claims.UserId.ToString("D")));
        identity.AddClaim(new Claim(ExpiryClaim, claims.ExpiresAt.ToUnixTimeSeconds().ToString()));
        foreach (var group in claims.Groups)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, group));
        }
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Reads the session claims back from a principal made by CreatePrincipal, or null when the principal is not a CRM-proof session.
    /// </summary>
    public static AuthTokenClaims? ReadClaims(ClaimsPrincipal? user)
    {
        if (user?.Identity is not { IsAuthenticated: true, AuthenticationType: AuthenticationType })
        {
            return null;
        }
        var account = user.FindFirst(ClaimTypes.Name)?.Value;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var exp = user.FindFirst(ExpiryClaim)?.Value;
        if (string.IsNullOrWhiteSpace(account) || !Guid.TryParse(id, out var userId) || !long.TryParse(exp, out var unix))
        {
            return null;
        }
        return new AuthTokenClaims(account, userId, user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(), DateTimeOffset.FromUnixTimeSeconds(unix));
    }
}
