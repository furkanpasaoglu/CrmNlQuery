using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Quality;

/// <summary>
/// Keeps quality runs and results in memory, used when no database is configured and in tests.
/// </summary>
public sealed class MemoryQualityRunStore : IQualityRunStore
{
    private readonly object _gate = new object();
    private readonly List<QualityRun> _runs = new List<QualityRun>();
    private readonly List<QualityResult> _results = new List<QualityResult>();

    /// <summary>
    /// Adds a quality run to the in-memory store.
    /// </summary>
    public Task AddRunAsync(QualityRun run, CancellationToken ct = default)
    {
        lock (_gate) { _runs.Add(run); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces the stored in-memory quality run that has the same id, doing nothing if it is not found.
    /// </summary>
    public Task UpdateRunAsync(QualityRun run, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var i = _runs.FindIndex(r => r.Id == run.Id);
            if (i >= 0) { _runs[i] = run; }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns a quality run by id from memory, or null.
    /// </summary>
    public Task<QualityRun?> GetRunAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_runs.FirstOrDefault(r => r.Id == id)); }
    }

    /// <summary>
    /// Lists recent quality runs from memory, newest first.
    /// </summary>
    public Task<IReadOnlyList<QualityRun>> ListRunsAsync(int take, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<QualityRun>>(_runs.OrderByDescending(r => r.StartedAt).Take(take).ToList()); }
    }

    /// <summary>
    /// Returns the most recent finished run before the given one, skipping unfinished runs.
    /// </summary>
    public Task<QualityRun?> PreviousDoneRunAsync(QualityRun run, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_runs.Where(r => r.Status == QualityRunStatus.Done && r.StartedAt < run.StartedAt && r.Id != run.Id).OrderByDescending(r => r.StartedAt).FirstOrDefault());
        }
    }

    /// <summary>
    /// Adds a case result to the in-memory store.
    /// </summary>
    public Task AddResultAsync(QualityResult result, CancellationToken ct = default)
    {
        lock (_gate) { _results.Add(result); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Lists the stored case results of a run.
    /// </summary>
    public Task<IReadOnlyList<QualityResult>> ListResultsAsync(string runId, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<QualityResult>>(_results.Where(r => r.RunId == runId).ToList()); }
    }

    /// <summary>
    /// Marks every unfinished run as failed with the given reason and returns the count.
    /// </summary>
    public Task<int> FailActiveAsync(string reason, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var n = 0;
            for (var i = 0; i < _runs.Count; i++)
            {
                if (QualityRunStatus.IsActive(_runs[i].Status)) { _runs[i] = _runs[i] with { Status = QualityRunStatus.Failed, Error = reason, FinishedAt = DateTimeOffset.UtcNow }; n++; }
            }
            return Task.FromResult(n);
        }
    }

    /// <summary>
    /// Removes finished runs older than the cutoff with their results and returns the count.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var old = _runs.Where(r => !QualityRunStatus.IsActive(r.Status) && (r.FinishedAt ?? r.StartedAt) < cutoff).Select(r => r.Id).ToHashSet();
            _runs.RemoveAll(r => old.Contains(r.Id));
            _results.RemoveAll(r => old.Contains(r.RunId));
            return Task.FromResult(old.Count);
        }
    }
}
