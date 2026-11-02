using CrmNlQuery.Application.Suggestions.Abstractions;
using CrmNlQuery.Application.Suggestions.Models;
using CrmNlQuery.Application.Suggestions.Services;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// In-memory suggestion stats used when no database is configured and in tests.
/// </summary>
public sealed class MemorySuggestionStats : ISuggestionStats
{
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new object();
    private readonly Dictionary<(DateOnly Day, string Kind), (int Shown, int Clicked)> _rows = new Dictionary<(DateOnly, string), (int, int)>();

    public MemorySuggestionStats(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    private DateOnly Today() => DateOnly.FromDateTime(_now().UtcDateTime);

    /// <summary>
    /// Records in memory which suggestion kinds were shown, counting per day.
    /// </summary>
    public Task AddShownAsync(IReadOnlyList<string> kinds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var kind in kinds.Where(SuggestionKinds.IsKnown))
            {
                var key = (Today(), kind);
                _rows.TryGetValue(key, out var old);
                _rows[key] = (old.Shown + 1, old.Clicked);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records in memory a click on a suggestion kind for today's counter.
    /// </summary>
    public Task AddClickAsync(string kind, CancellationToken ct = default)
    {
        if (SuggestionKinds.IsKnown(kind))
        {
            lock (_gate)
            {
                var key = (Today(), kind);
                _rows.TryGetValue(key, out var old);
                _rows[key] = (old.Shown, old.Clicked + 1);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns per-kind shown and clicked totals for the last given number of days from memory.
    /// </summary>
    public Task<IReadOnlyList<SuggestionStat>> SummaryAsync(int days, CancellationToken ct = default)
    {
        var from = Today().AddDays(-(Math.Max(1, days) - 1));
        lock (_gate)
        {
            IReadOnlyList<SuggestionStat> list = _rows.Where(r => r.Key.Day >= from)
                .GroupBy(r => r.Key.Kind)
                .Select(g => new SuggestionStat(g.Key, g.Sum(r => r.Value.Shown), g.Sum(r => r.Value.Clicked)))
                .OrderBy(s => SuggestionKinds.All.ToList().IndexOf(s.Kind)).ToList();
            return Task.FromResult(list);
        }
    }

    /// <summary>
    /// Removes in-memory suggestion counters older than the cutoff and returns how many were removed.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(cutoff.UtcDateTime);
        lock (_gate)
        {
            var old = _rows.Keys.Where(k => k.Day < day).ToList();
            foreach (var key in old)
            {
                _rows.Remove(key);
            }
            return Task.FromResult(old.Count);
        }
    }
}
