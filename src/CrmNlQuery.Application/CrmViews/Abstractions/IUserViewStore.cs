using CrmNlQuery.Application.CrmViews.Models;

namespace CrmNlQuery.Application.CrmViews.Abstractions;

/// <summary>
/// Stores the CRM user's personal views, with every operation done as the user so the CRM applies its own security.
/// </summary>
public interface IUserViewStore
{
    /// <summary>
    /// Creates the caller's view with this name or updates the first one if it exists.
    /// </summary>
    Task<string> UpsertAsync(Guid caller, string name, ViewDefinition view, CancellationToken ct = default);

    /// <summary>
    /// Deletes the caller's views with this name, doing nothing if none exist.
    /// </summary>
    Task DeleteAsync(Guid caller, string name, CancellationToken ct = default);
}
