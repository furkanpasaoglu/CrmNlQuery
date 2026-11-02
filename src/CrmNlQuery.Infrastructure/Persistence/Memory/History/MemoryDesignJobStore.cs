using CrmNlQuery.Application.Design.Abstractions;
using CrmNlQuery.Application.Design.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.History;

/// <summary>
/// In-memory design job store used when no database is configured and in tests.
/// </summary>
public sealed class MemoryDesignJobStore : IDesignJobStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, DesignJob> _jobs = new Dictionary<string, DesignJob>(StringComparer.Ordinal);

    private static DesignJob Copy(DesignJob job) => new DesignJob
    {
        Id = job.Id, Owner = job.Owner, Status = job.Status, Step = job.Step, Percent = job.Percent, Title = job.Title, Prefix = job.Prefix, Request = job.Request,
        Result = job.Result, Error = job.Error, ProblemCount = job.ProblemCount, CreatedAt = job.CreatedAt, UpdatedAt = job.UpdatedAt, FinishedAt = job.FinishedAt,
    };

    /// <summary>
    /// Stores a design job in memory (test and lab implementation).
    /// </summary>
    public Task AddAsync(DesignJob job, CancellationToken ct = default)
    {
        lock (_gate) { _jobs[job.Id] = Copy(job); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces an existing in-memory design job with the updated one.
    /// </summary>
    public Task UpdateAsync(DesignJob job, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_jobs.ContainsKey(job.Id))
            {
                _jobs[job.Id] = Copy(job);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns one design job only if it belongs to the given owner, otherwise null.
    /// </summary>
    public Task<DesignJob?> GetAsync(string owner, string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_jobs.TryGetValue(id, out var job) && string.Equals(job.Owner, owner, StringComparison.OrdinalIgnoreCase) ? Copy(job) : null);
        }
    }

    /// <summary>
    /// Lists the owner's design jobs, newest first, up to the requested count, from memory.
    /// </summary>
    public Task<IReadOnlyList<DesignJob>> ListAsync(string owner, int take, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<DesignJob> list = _jobs.Values.Where(j => string.Equals(j.Owner, owner, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(j => j.CreatedAt).Take(Math.Max(1, take))
                .Select(j => { var c = Copy(j); c.Result = null; return c; }).ToList();
            return Task.FromResult(list);
        }
    }

    /// <summary>
    /// Deletes an owner's design job from memory and reports whether it existed.
    /// </summary>
    public Task<bool> DeleteAsync(string owner, string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_jobs.TryGetValue(id, out var job) && string.Equals(job.Owner, owner, StringComparison.OrdinalIgnoreCase) && _jobs.Remove(id));
        }
    }

    /// <summary>
    /// Creates a failed result for active in memory.
    /// </summary>
    public Task<int> FailActiveAsync(string reason, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var active = _jobs.Values.Where(j => DesignJobStatus.IsActive(j.Status)).ToList();
            foreach (var job in active)
            {
                job.Status = DesignJobStatus.Failed;
                job.Error = reason;
                job.Step = "Failed";
                job.FinishedAt = DateTimeOffset.UtcNow;
            }
            return Task.FromResult(active.Count);
        }
    }

    /// <summary>
    /// Removes in-memory design jobs older than the cutoff and returns how many were removed.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var old = _jobs.Values.Where(j => j.CreatedAt < cutoff && !DesignJobStatus.IsActive(j.Status)).Select(j => j.Id).ToList();
            foreach (var id in old) { _jobs.Remove(id); }
            return Task.FromResult(old.Count);
        }
    }
}
