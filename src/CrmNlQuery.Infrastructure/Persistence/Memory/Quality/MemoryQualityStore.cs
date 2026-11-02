using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Quality;

/// <summary>
/// Stores the acceptance set in memory when no database is configured and in tests, losing cases when the pool restarts.
/// </summary>
public sealed class MemoryQualityStore : IQualityStore
{
    private readonly object _gate = new object();
    private readonly List<QualityCase> _cases = new List<QualityCase>();

    public bool Persistent => false;

    /// <summary>
    /// Adds a case to the in-memory acceptance set.
    /// </summary>
    public Task AddAsync(QualityCase item, CancellationToken ct = default)
    {
        lock (_gate) { _cases.Add(item); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the in-memory acceptance-set cases matching the optional status, source, verified and entity filters, under the store lock.
    /// </summary>
    public Task<IReadOnlyList<QualityCase>> ListAsync(QualityCaseFilter filter, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<QualityCase> result = _cases
                .Where(c => (filter.Status is null || c.Status == filter.Status)
                    && (filter.Source is null || c.Source == filter.Source)
                    && (filter.Verified is null || c.Verified == filter.Verified)
                    && (filter.Entity is null || c.Entity == filter.Entity))
                .OrderBy(c => c.Entity, StringComparer.Ordinal).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Gets a case by id, or null.
    /// </summary>
    public Task<QualityCase?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_cases.FirstOrDefault(c => c.Id == id)); }
    }

    /// <summary>
    /// Returns how many acceptance-set cases the in-memory store currently holds, read under the store lock.
    /// </summary>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_cases.Count); }
    }

    /// <summary>
    /// Tells whether a case with the same question and reference query already exists.
    /// </summary>
    public Task<bool> ExistsAsync(string question, string referenceFetch, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_cases.Any(c => c.Question == question && c.ReferenceFetch == referenceFetch)); }
    }

    /// <summary>
    /// Sets the status of the given cases and returns how many changed.
    /// </summary>
    public Task<int> SetStatusAsync(IReadOnlyCollection<string> ids, string status, CancellationToken ct = default) => Update(ids, c => c with { Status = status });

    /// <summary>
    /// Sets the verified flag of the given cases and returns how many changed.
    /// </summary>
    public Task<int> SetVerifiedAsync(IReadOnlyCollection<string> ids, bool verified, CancellationToken ct = default) => Update(ids, c => c with { Verified = verified });

    private Task<int> Update(IReadOnlyCollection<string> ids, Func<QualityCase, QualityCase> change)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        var n = 0;
        lock (_gate)
        {
            for (var i = 0; i < _cases.Count; i++)
            {
                if (set.Contains(_cases[i].Id)) { _cases[i] = change(_cases[i]); n++; }
            }
        }
        return Task.FromResult(n);
    }

    /// <summary>
    /// Deletes the given cases and returns how many were removed.
    /// </summary>
    public Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        lock (_gate) { return Task.FromResult(_cases.RemoveAll(c => set.Contains(c.Id))); }
    }
}
