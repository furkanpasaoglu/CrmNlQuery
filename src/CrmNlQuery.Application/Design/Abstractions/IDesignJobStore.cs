using CrmNlQuery.Application.Design.Models;

namespace CrmNlQuery.Application.Design.Abstractions;

/// <summary>
/// Stores design jobs, visible only to their owner.
/// </summary>
public interface IDesignJobStore
{
    /// <summary>
    /// Inserts a new design job into the design job store.
    /// </summary>
    Task AddAsync(DesignJob job, CancellationToken ct = default);

    /// <summary>
    /// Replaces an existing design job in the store, used to save progress, results and errors.
    /// </summary>
    Task UpdateAsync(DesignJob job, CancellationToken ct = default);

    /// <summary>
    /// Returns the design job only for its owner, or null.
    /// </summary>
    Task<DesignJob?> GetAsync(string owner, string id, CancellationToken ct = default);

    /// <summary>
    /// Lists the owner jobs newest first without loading the large result column.
    /// </summary>
    Task<IReadOnlyList<DesignJob>> ListAsync(string owner, int take, CancellationToken ct = default);

    /// <summary>
    /// Deletes the given owner's design job by id and returns whether it was removed.
    /// </summary>
    Task<bool> DeleteAsync(string owner, string id, CancellationToken ct = default);

    /// <summary>
    /// Marks queued or running jobs as failed after a server restart and returns how many.
    /// </summary>
    Task<int> FailActiveAsync(string reason, CancellationToken ct = default);

    /// <summary>
    /// Deletes design jobs older than the cutoff.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
