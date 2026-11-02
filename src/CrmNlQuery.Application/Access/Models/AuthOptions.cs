namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Settings for the CRM-proof sign-in: signing key, token lifetime and challenge lifetime.
/// </summary>
public sealed class AuthOptions
{
    public string SigningKey { get; set; } = "";
    public int TokenHours { get; set; } = 8;
    public int ChallengeSeconds { get; set; } = 120;
}
