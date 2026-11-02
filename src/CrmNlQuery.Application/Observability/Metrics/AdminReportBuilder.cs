
namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Builds the admin report from a metrics summary and time range.
/// </summary>
public static class AdminReportBuilder
{
    public static readonly IReadOnlyList<string> BlockedKinds = new List<string>
    {
        "asks_paused", "rate_limited", "daily_quota", "busy_user", "busy_total", "budget_exceeded", "question_too_long",
    };

    /// <summary>
    /// Builds an admin report from a metrics summary for a date range.
    /// </summary>
    public static AdminReport Build(MetricsSummary s, DateTimeOffset from, DateTimeOffset to,
        IReadOnlyDictionary<string, ModelPrice>? prices, IReadOnlyDictionary<Guid, string> names)
    {
        var models = s.Models
            .Select(m => new ModelUsageCost(m.Provider, m.Model, m.Calls, m.InputTokens, m.OutputTokens,
                CostEstimator.Estimate(m.Provider, m.Model, m.InputTokens, m.OutputTokens, prices)))
            .ToList();
        var known = models.Where(m => m.EstimatedCostUsd is not null).ToList();
        double? total = models.Count == 0 ? 0.0 : known.Count == 0 ? null : known.Sum(m => m.EstimatedCostUsd!.Value);

        var blocked = s.FailuresByType.Where(f => BlockedKinds.Contains(f.Key)).ToList();
        var users = s.TopUsers.Select(u => new UserRow(u.User, names.TryGetValue(u.User, out var name) ? name : null, u.Requests)).ToList();

        return new AdminReport(from, to,
            s.Requests, s.Answers, s.Questions, s.Failures, s.AiCalls, s.AiErrors, s.InputTokens, s.OutputTokens,
            s.RequestP50Ms, s.RequestP95Ms,
            s.Requests - s.Cancelled <= 0 ? 0 : (double)s.Failures / (s.Requests - s.Cancelled),
            s.Requests == 0 ? 0 : (double)s.AiCalls / s.Requests,
            s.AiCalls == 0 ? 0 : (double)s.AiErrors / s.AiCalls,
            total, models.Any(m => m.EstimatedCostUsd is null),
            FillDays(s.Days, from, to), s.Latency, models, s.FailuresByType, s.AiErrorsByType, users,
            blocked.Sum(b => b.Count), blocked,
            s.KnowledgeSearched, s.KnowledgeUsed, s.KnowledgePieces,
            s.KnowledgeSearched == 0 ? 0 : (double)s.KnowledgeUsed / s.KnowledgeSearched,
            s.KnowledgeSearched == 0 ? 0 : (double)s.KnowledgePieces / s.KnowledgeSearched,
            s.FollowUps, s.Answers == 0 ? 0 : (double)s.FollowUps / s.Answers,
            s.Cancelled);
    }

    private static IReadOnlyList<DayPoint> FillDays(IReadOnlyList<DayPoint> days, DateTimeOffset from, DateTimeOffset to)
    {
        var byDay = days.ToDictionary(d => d.Day);
        var result = new List<DayPoint>();
        var last = DateOnly.FromDateTime(to.AddTicks(-1).UtcDateTime);
        for (var d = DateOnly.FromDateTime(from.UtcDateTime); d <= last; d = d.AddDays(1))
        {
            result.Add(byDay.TryGetValue(d, out var point) ? point : new DayPoint(d, 0, 0, 0, 0, 0));
        }
        return result;
    }
}
