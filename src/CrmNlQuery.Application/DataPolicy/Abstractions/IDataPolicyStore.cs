using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.DataPolicy.Abstractions;

/// <summary>
/// Port for persisting data policy rules for tables and fields.
/// </summary>
public interface IDataPolicyStore
{
    bool Persistent { get; }

    /// <summary>
    /// Returns all stored data policy rules, the administrator's explicit table and field decisions, from the store.
    /// </summary>
    Task<IReadOnlyList<DataPolicyRule>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves a rule, replacing any rule with the same kind, table and field; an invalid rule throws ArgumentException.
    /// </summary>
    Task SetAsync(DataPolicyRule rule, CancellationToken ct = default);

    /// <summary>
    /// Removes a rule if it exists, restoring the default, and returns whether it was removed.
    /// </summary>
    Task<bool> RemoveAsync(string kind, string entity, string? attribute, CancellationToken ct = default);
}
