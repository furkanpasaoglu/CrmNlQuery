namespace CrmNlQuery.Application.Access.Abstractions;

/// <summary>
/// Resolves Windows accounts to CRM users.
/// </summary>
public interface ICallerResolver
{
    /// <summary>
    /// Resolves a Windows account name (DOMAIN\user) to the active CRM user's system user id, or null.
    /// </summary>
    Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default);
}
