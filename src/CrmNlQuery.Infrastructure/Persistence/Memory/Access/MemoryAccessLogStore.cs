using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// In-memory append-only data access log store used when no database is configured and in tests; retention purge is the only delete path.
/// </summary>
public sealed class MemoryAccessLogStore : IAccessLogStore, CrmNlQuery.Application.Export.Abstractions.IAccessLogPager
{
    private readonly object _gate = new object();
    private readonly int _capacity;
    private readonly List<DataAccessRecord> _records = new List<DataAccessRecord>();

    public MemoryAccessLogStore(int capacity = 20000)
    {
        _capacity = capacity;
    }

    /// <summary>
    /// Adds one data access record to the in-memory log.
    /// </summary>
    public Task AddAsync(DataAccessRecord record, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _records.Add(record);
            if (_records.Count > _capacity)
            {
                _records.RemoveRange(0, _records.Count - _capacity);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pages access log rows oldest to newest with a time and id cursor, for exports.
    /// </summary>
    public Task<IReadOnlyList<DataAccessRecord>> PageAsync(DataAccessQuery query, CrmNlQuery.Application.Export.Models.AccessCursor? after, int size, CancellationToken ct = default)
    {
        var table = (query.Table ?? "").Trim().ToLowerInvariant();
        var source = (query.Source ?? "").Trim();
        lock (_gate)
        {
            var page = _records
                .Where(r => r.At >= query.From && r.At < query.To)
                .Where(r => query.User is null || r.User == query.User)
                .Where(r => table.Length == 0 || r.Tables.Any(t => string.Equals(t, table, StringComparison.OrdinalIgnoreCase)))
                .Where(r => source.Length == 0 || string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.At).ThenBy(r => r.Id)
                .Where(r => after is null || r.At > after.At || (r.At == after.At && r.Id.CompareTo(after.Id) > 0))
                .Take(Math.Max(1, size)).ToList();
            return Task.FromResult<IReadOnlyList<DataAccessRecord>>(page);
        }
    }

    /// <summary>
    /// Queries in-memory access log records using the given filters.
    /// </summary>
    public Task<IReadOnlyList<DataAccessRecord>> QueryAsync(DataAccessQuery query, CancellationToken ct = default)
    {
        var take = Math.Clamp(query.Take, 1, 500);
        var table = (query.Table ?? "").Trim().ToLowerInvariant();
        lock (_gate)
        {
            var found = _records
                .Where(r => r.At >= query.From && r.At < query.To)
                .Where(r => query.User is null || r.User == query.User)
                .Where(r => table.Length == 0 || r.Tables.Contains(table))
                .OrderByDescending(r => r.At)
                .Take(take)
                .ToList();
            return Task.FromResult<IReadOnlyList<DataAccessRecord>>(found);
        }
    }

    /// <summary>
    /// Deletes in-memory records older than a cutoff for retention.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_records.RemoveAll(r => r.At < cutoff));
        }
    }
}
