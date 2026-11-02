using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Application.Quality.Abstractions;

/// <summary>
/// Stores quality check runs and their results, never result row data.
/// </summary>
public interface IQualityRunStore
{
    /// <summary>
    /// Storage contract that inserts a new quality run record.
    /// </summary>
    Task AddRunAsync(QualityRun run, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that replaces an existing quality run record, used to save progress and the final status.
    /// </summary>
    Task UpdateRunAsync(QualityRun run, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that returns the quality run with the given id, or null when none exists.
    /// </summary>
    Task<QualityRun?> GetRunAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that returns up to the given number of quality runs, newest first.
    /// </summary>
    Task<IReadOnlyList<QualityRun>> ListRunsAsync(int take, CancellationToken ct = default);
    /// <summary>
    /// Returns the last finished run that started before the given run, for regression comparison, or null.
    /// </summary>
    Task<QualityRun?> PreviousDoneRunAsync(QualityRun run, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that stores the result of one case within a quality run.
    /// </summary>
    Task AddResultAsync(QualityResult result, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that returns the stored case results of the given quality run.
    /// </summary>
    Task<IReadOnlyList<QualityResult>> ListResultsAsync(string runId, CancellationToken ct = default);
    /// <summary>
    /// Marks runs left queued or running after a server restart as failed and returns their count.
    /// </summary>
    Task<int> FailActiveAsync(string reason, CancellationToken ct = default);
    /// <summary>
    /// Deletes runs finished before the cutoff, with their results, and returns the count.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
