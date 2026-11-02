namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// What a signed session token carries: who the user is, their CRM user id, their group names and when it expires.
/// </summary>
public sealed record AuthTokenClaims(string Account, Guid UserId, IReadOnlyList<string> Groups, DateTimeOffset ExpiresAt);
