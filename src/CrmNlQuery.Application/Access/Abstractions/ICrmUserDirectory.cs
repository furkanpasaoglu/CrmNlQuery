using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Application.Access.Abstractions;

/// <summary>
/// Looks up a CRM user by system user id for the sign-in.
/// </summary>
public interface ICrmUserDirectory
{
    /// <summary>
    /// Returns the user with account name, disabled flag and group names, or null if the user does not exist.
    /// </summary>
    Task<CrmUserRecord?> GetAsync(Guid userId, CancellationToken ct = default);
}
