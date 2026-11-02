namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Outcome of a CRM-proof sign-in: a session token with its account and expiry, or the reason it was refused.
/// </summary>
public sealed record LoginResult(string? Token, string? Account, DateTimeOffset? ExpiresAt, LoginError Error)
{
    public bool Ok => Error == LoginError.None && Token is not null;

    /// <summary>
    /// Creates a refused result with the given reason.
    /// </summary>
    public static LoginResult Refused(LoginError error) => new LoginResult(null, null, null, error);
}
