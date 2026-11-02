
namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds the admin dashboard report read in one go, with no row data or question text.
/// </summary>
public sealed record AdminReport(
    DateTimeOffset From, DateTimeOffset To,
    int Requests, int Answers, int Questions, int Failures, int AiCalls, int AiErrors, long InputTokens, long OutputTokens,
    double RequestP50Ms, double RequestP95Ms, double FailureRate, double AiCallsPerRequest, double AiErrorRate,
    double? TotalCostUsd, bool CostIsPartial,
    IReadOnlyList<DayPoint> Days, IReadOnlyList<LatencyPoint> Latency, IReadOnlyList<ModelUsageCost> Models,
    IReadOnlyList<CountBy> FailuresByType, IReadOnlyList<CountBy> AiErrorsByType, IReadOnlyList<UserRow> Users,
    int Blocked = 0, IReadOnlyList<CountBy>? BlockedByType = null,
    int KnowledgeSearched = 0, int KnowledgeUsed = 0, long KnowledgePieces = 0, double KnowledgeUsedRate = 0, double KnowledgeAveragePieces = 0,
    int FollowUps = 0, double FollowUpRate = 0,
    int Cancelled = 0);
