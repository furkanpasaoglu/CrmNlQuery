using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// In-memory report stats used when no database is configured and in tests.
/// </summary>
public sealed class MemoryReportStats : IReportStats
{
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new object();
    private readonly Dictionary<(DateOnly Day, string Name), (int Shown, int Opened)> _rows = new Dictionary<(DateOnly, string), (int, int)>();

    public MemoryReportStats(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    private DateOnly Today() => DateOnly.FromDateTime(_now().UtcDateTime);

    /// <summary>
    /// Counts in memory that the named reports were shown today.
    /// </summary>
    public Task AddShownAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var raw in names)
            {
                var name = ReportStatName.Clean(raw);
                if (name is null) { continue; }
                var key = (Today(), name);
                _rows.TryGetValue(key, out var old);
                _rows[key] = (old.Shown + 1, old.Opened);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Counts in memory that the named report was opened today.
    /// </summary>
    public Task AddOpenedAsync(string name, CancellationToken ct = default)
    {
        var clean = ReportStatName.Clean(name);
        if (clean is not null)
        {
            lock (_gate)
            {
                var key = (Today(), clean);
                _rows.TryGetValue(key, out var old);
                _rows[key] = (old.Shown, old.Opened + 1);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns per-report shown and opened totals for the last days, most opened first, limited to top rows, from memory.
    /// </summary>
    public Task<IReadOnlyList<ReportStat>> SummaryAsync(int days, int top, CancellationToken ct = default)
    {
        var from = Today().AddDays(-(Math.Max(1, days) - 1));
        lock (_gate)
        {
            IReadOnlyList<ReportStat> list = _rows.Where(r => r.Key.Day >= from)
                .GroupBy(r => r.Key.Name)
                .Select(g => new ReportStat(g.Key, g.Sum(r => r.Value.Shown), g.Sum(r => r.Value.Opened)))
                .OrderByDescending(s => s.Opened).ThenByDescending(s => s.Shown).ThenBy(s => s.Name, StringComparer.Ordinal)
                .Take(Math.Max(1, top)).ToList();
            return Task.FromResult(list);
        }
    }

    /// <summary>
    /// Removes in-memory report counters older than the cutoff and returns how many were removed.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(cutoff.UtcDateTime);
        lock (_gate)
        {
            var old = _rows.Keys.Where(k => k.Day < day).ToList();
            foreach (var key in old) { _rows.Remove(key); }
            return Task.FromResult(old.Count);
        }
    }
}
