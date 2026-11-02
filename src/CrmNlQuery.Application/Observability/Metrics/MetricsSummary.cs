namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Aggregated usage, latency, failure and cost summary for the admin dashboard.
/// </summary>
public sealed record MetricsSummary(
    int Requests, int Answers, int Questions, int Failures,
    int AiCalls, int AiErrors, long InputTokens, long OutputTokens,
    double RequestP50Ms, double RequestP95Ms,
    IReadOnlyList<DayPoint> Days, IReadOnlyList<LatencyPoint> Latency, IReadOnlyList<ModelUsage> Models,
    IReadOnlyList<CountBy> FailuresByType, IReadOnlyList<CountBy> AiErrorsByType, IReadOnlyList<UserUsage> TopUsers,
    int KnowledgeSearched = 0, int KnowledgeUsed = 0, long KnowledgePieces = 0,
    int FollowUps = 0,
    int Cancelled = 0);
