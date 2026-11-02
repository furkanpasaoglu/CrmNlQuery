namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// A one-time challenge the browser must prove in the CRM: the nonce, the record subject to create and its lifetime.
/// </summary>
public sealed record LoginChallenge(string Nonce, string Subject, int ExpiresInSeconds);
