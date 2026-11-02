using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

/// <summary>
/// Keeps the report catalog in memory when no database is configured and in tests.
/// </summary>
public sealed class MemoryReportCatalogStore : IReportCatalogStore
{
    private readonly object _gate = new object();
    private readonly List<ReportCatalogEntry> _entries = new List<ReportCatalogEntry>();

    public bool Persistent => false;

    /// <summary>
    /// Returns all in-memory report catalog entries sorted by name, culture-aware and case-insensitive, read under the store lock.
    /// </summary>
    public Task<IReadOnlyList<ReportCatalogEntry>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<ReportCatalogEntry>>(_entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList()); }
    }

    /// <summary>
    /// Returns a catalog entry by id, or null.
    /// </summary>
    public Task<ReportCatalogEntry?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_entries.FirstOrDefault(e => e.Id == id)); }
    }

    /// <summary>
    /// Inserts or updates a catalog entry.
    /// </summary>
    public Task UpsertAsync(ReportCatalogEntry entry, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var i = _entries.FindIndex(e => e.Id == entry.Id);
            if (i < 0) { _entries.Add(entry); } else { _entries[i] = entry; }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the report catalog entry with the given id from memory, returning whether it existed.
    /// </summary>
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0); }
    }
}
