namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// A CRM user as the sign-in needs it: id, Windows-style account name, whether disabled and the group names (roles and teams).
/// </summary>
public sealed record CrmUserRecord(Guid UserId, string Account, bool Disabled, IReadOnlyList<string> Groups);
