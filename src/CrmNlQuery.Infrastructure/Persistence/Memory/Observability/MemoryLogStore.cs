using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// In-memory log store used when no database is configured and in tests.
/// </summary>
public sealed class MemoryLogStore : ILogStore
{
    private readonly object _gate = new object();
    private readonly int _capacity;
    private readonly List<LogEntry> _entries = new List<LogEntry>();

    public MemoryLogStore(int capacity = 5000)
    {
        _capacity = capacity;
    }

    /// <summary>
    /// Appends log entries to the in-memory log store.
    /// </summary>
    public Task AddAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _entries.AddRange(entries);
            if (_entries.Count > _capacity)
            {
                _entries.RemoveRange(0, _entries.Count - _capacity);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns in-memory log entries matching the query, newest first.
    /// </summary>
    public Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var text = string.IsNullOrEmpty(query.Text) ? null : query.Text;
            var found = _entries.Select((entry, index) => (entry, index))
                .Where(x => x.entry.Level >= query.MinLevel)
                .Where(x => query.From is null || x.entry.At >= query.From)
                .Where(x => text is null || Matches(x.entry, text))
                .OrderByDescending(x => x.entry.At).ThenByDescending(x => x.index)
                .Take(query.Take)
                .Select(x => x.entry)
                .ToList();
            return Task.FromResult<IReadOnlyList<LogEntry>>(found);
        }
    }

    /// <summary>
    /// Removes in-memory log entries older than the cutoff and returns how many were removed.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.RemoveAll(e => e.At < cutoff));
        }
    }

    private static bool Matches(LogEntry entry, string text)
    {
        return entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (entry.Exception is not null && entry.Exception.Contains(text, StringComparison.OrdinalIgnoreCase))
            || string.Equals(entry.RequestId, text, StringComparison.OrdinalIgnoreCase);
    }
}
