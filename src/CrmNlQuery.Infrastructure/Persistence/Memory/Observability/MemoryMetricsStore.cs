using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// In-memory metrics store used when no database is configured and in tests, lost on restart.
/// </summary>
public sealed class MemoryMetricsStore : IMetricsStore
{
    private readonly object _gate = new object();
    private readonly List<AiCallRecord> _calls = new List<AiCallRecord>();
    private readonly List<RequestRecord> _requests = new List<RequestRecord>();

    /// <summary>
    /// Appends a model call record to the in-memory list under the store lock.
    /// </summary>
    public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default)
    {
        lock (_gate) { _calls.Add(record); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Appends a request record to the in-memory list under the store lock.
    /// </summary>
    public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default)
    {
        lock (_gate) { _requests.Add(record); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the metrics summary for a date range from memory.
    /// </summary>
    public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        List<AiCallRecord> calls;
        List<RequestRecord> requests;
        lock (_gate)
        {
            calls = _calls.Where(c => c.At >= from && c.At < to).ToList();
            requests = _requests.Where(r => r.At >= from && r.At < to).ToList();
        }

        var requestDurations = requests.Select(r => (double)r.DurationMs).ToList();
        var days = requests.Select(r => DateOnly.FromDateTime(r.At.UtcDateTime))
            .Concat(calls.Select(c => DateOnly.FromDateTime(c.At.UtcDateTime)))
            .Distinct().OrderBy(d => d)
            .Select(d => new DayPoint(
                d,
                requests.Count(r => DateOnly.FromDateTime(r.At.UtcDateTime) == d),
                requests.Count(r => DateOnly.FromDateTime(r.At.UtcDateTime) == d && r.Outcome == "failure"),
                calls.Count(c => DateOnly.FromDateTime(c.At.UtcDateTime) == d),
                calls.Where(c => DateOnly.FromDateTime(c.At.UtcDateTime) == d).Sum(c => (long)(c.InputTokens ?? 0)),
                calls.Where(c => DateOnly.FromDateTime(c.At.UtcDateTime) == d).Sum(c => (long)(c.OutputTokens ?? 0))))
            .ToList();

        var latency = calls.GroupBy(c => c.Purpose).OrderBy(g => g.Key)
            .Select(g =>
            {
                var durations = g.Select(c => (double)c.DurationMs).ToList();
                var sized = g.Where(c => c.PromptChars is not null).ToList();
                return new LatencyPoint(g.Key, g.Count(), g.Count(c => c.Outcome == "error"),
                    Percentile(durations, 0.5), Percentile(durations, 0.95),
                    sized.Count == 0 ? null : sized.Average(c => (double)c.PromptChars!.Value),
                    sized.Count == 0 ? null : sized.Average(c => (double)(c.EstTokens ?? 0)));
            }).ToList();

        var models = calls.GroupBy(c => (c.Provider, c.Model)).OrderByDescending(g => g.Count())
            .Select(g => new ModelUsage(g.Key.Provider, g.Key.Model, g.Count(),
                g.Sum(c => (long)(c.InputTokens ?? 0)), g.Sum(c => (long)(c.OutputTokens ?? 0))))
            .ToList();

        var failures = requests.Where(r => r.Outcome == "failure")
            .GroupBy(r => r.ErrorType ?? "other").Select(g => new CountBy(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Key).ToList();

        var aiErrors = calls.Where(c => c.Outcome == "error")
            .GroupBy(c => c.ErrorType ?? "other").Select(g => new CountBy(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Key).ToList();

        var users = requests.Where(r => r.User is not null)
            .GroupBy(r => r.User!.Value).Select(g => new UserUsage(g.Key, g.Count()))
            .OrderByDescending(u => u.Requests).Take(20).ToList();

        var summary = new MetricsSummary(
            requests.Count,
            requests.Count(r => r.Outcome == "answer"),
            requests.Count(r => r.Outcome == "question"),
            requests.Count(r => r.Outcome == "failure"),
            calls.Count,
            calls.Count(c => c.Outcome == "error"),
            calls.Sum(c => (long)(c.InputTokens ?? 0)),
            calls.Sum(c => (long)(c.OutputTokens ?? 0)),
            Percentile(requestDurations, 0.5),
            Percentile(requestDurations, 0.95),
            days, latency, models, failures, aiErrors, users,
            requests.Count(r => r.KnowledgePieces is not null),
            requests.Count(r => r.KnowledgePieces is > 0),
            requests.Sum(r => (long)(r.KnowledgePieces ?? 0)),
            requests.Count(r => r.FollowUp == true),
            requests.Count(r => r.Outcome == "cancelled"));
        return Task.FromResult(summary);
    }

    /// <summary>
    /// Deletes in-memory records older than the cutoff.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var removed = _calls.RemoveAll(c => c.At < cutoff) + _requests.RemoveAll(r => r.At < cutoff);
            return Task.FromResult(removed);
        }
    }

    private static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var sorted = values.OrderBy(v => v).ToList();
        var position = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }
}
