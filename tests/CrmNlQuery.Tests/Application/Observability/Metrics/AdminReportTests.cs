using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Tests.Application.Observability.Metrics;

/// <summary>
/// Tests the admin usage report: rates, daily series, model costs, blocked requests, knowledge and follow-up figures.
/// </summary>
public class AdminReportTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static MetricsSummary Summary(int requests = 10, int failures = 2, int aiCalls = 25, int aiErrors = 1,
        IReadOnlyList<DayPoint>? days = null, IReadOnlyList<ModelUsage>? models = null, IReadOnlyList<UserUsage>? users = null, int cancelled = 0)
        => new MetricsSummary(requests, requests - failures - 1, 1, failures, aiCalls, aiErrors, 5000, 800, 2000, 9000,
            days ?? new List<DayPoint>(), new List<LatencyPoint>(), models ?? new List<ModelUsage>(),
            new List<CountBy>(), new List<CountBy>(), users ?? new List<UserUsage>(), Cancelled: cancelled);

    /// <summary>
    /// Verifies that cancelled asks are reported and left out of the failure rate.
    /// </summary>
    [Fact]
    public void CancelledAsks_AreReported_AndLeftOutOfTheFailureRate()
    {
        var report = AdminReportBuilder.Build(Summary(requests: 10, failures: 2, cancelled: 2), From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(2, report.Cancelled);
        Assert.Equal(0.25, report.FailureRate, 3);
    }

    /// <summary>
    /// Verifies that the days are filled with zeros for the whole window.
    /// </summary>
    [Fact]
    public void Days_AreFilledWithZeros_ForTheWholeWindow()
    {
        var days = new List<DayPoint> { new DayPoint(new DateOnly(2026, 10, 12), 3, 1, 7, 100, 20) };

        var report = AdminReportBuilder.Build(Summary(days: days), From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(5, report.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 10), report.Days[0].Day);
        Assert.Equal(new DateOnly(2026, 10, 14), report.Days[4].Day);
        Assert.Equal(3, report.Days[2].Requests);
        Assert.Equal(0, report.Days[0].Requests);
        Assert.Equal(0, report.Days[4].AiCalls);
    }

    /// <summary>
    /// Verifies that rates are derived safely.
    /// </summary>
    [Fact]
    public void Rates_AreDerivedSafely()
    {
        var report = AdminReportBuilder.Build(Summary(requests: 10, failures: 2, aiCalls: 25, aiErrors: 1), From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(0.2, report.FailureRate, 3);
        Assert.Equal(2.5, report.AiCallsPerRequest, 3);
        Assert.Equal(0.04, report.AiErrorRate, 3);

        var empty = AdminReportBuilder.Build(Summary(requests: 0, failures: 0, aiCalls: 0, aiErrors: 0), From, To, null, new Dictionary<Guid, string>());
        Assert.Equal(0, empty.FailureRate);
        Assert.Equal(0, empty.AiCallsPerRequest);
        Assert.Equal(0, empty.AiErrorRate);
    }

    /// <summary>
    /// Verifies that models get an estimated cost and the total skips unknown prices.
    /// </summary>
    [Fact]
    public void Models_GetAnEstimatedCost_AndTheTotalSkipsUnknownPrices()
    {
        var models = new List<ModelUsage>
        {
            new ModelUsage("Anthropic", "claude-haiku-5-5", 20, 1_000_000, 200_000),
            new ModelUsage("Ollama", "qwen3.6:27b", 5, 40_000, 9_000),
            new ModelUsage("Anthropic", "claude-mystery-9", 1, 10, 10),
        };

        var report = AdminReportBuilder.Build(Summary(models: models), From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(0.20, report.Models[0].EstimatedCostUsd!.Value, 4);
        Assert.Equal(0.0, report.Models[1].EstimatedCostUsd!.Value, 4);
        Assert.Null(report.Models[2].EstimatedCostUsd);
        Assert.Equal(0.20, report.TotalCostUsd!.Value, 4);
        Assert.True(report.CostIsPartial);
    }

    /// <summary>
    /// Verifies that configured prices override the defaults.
    /// </summary>
    [Fact]
    public void ConfiguredPrices_OverrideTheDefaults()
    {
        var models = new List<ModelUsage> { new ModelUsage("Anthropic", "claude-haiku-5-5", 1, 1_000_000, 1_000_000) };
        var prices = new Dictionary<string, ModelPrice> { ["claude-haiku-5-5"] = new ModelPrice(1.0, 2.0) };

        var report = AdminReportBuilder.Build(Summary(models: models), From, To, prices, new Dictionary<Guid, string>());

        Assert.Equal(3.0, report.Models[0].EstimatedCostUsd!.Value, 4);
        Assert.False(report.CostIsPartial);
    }

    /// <summary>
    /// Verifies that users get their names when known.
    /// </summary>
    [Fact]
    public void Users_GetTheirNames_WhenKnown()
    {
        var known = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var users = new List<UserUsage> { new UserUsage(known, 7), new UserUsage(unknown, 2) };
        var names = new Dictionary<Guid, string> { [known] = "Ali Veli" };

        var report = AdminReportBuilder.Build(Summary(users: users), From, To, null, names);

        Assert.Equal("Ali Veli", report.Users[0].Name);
        Assert.Null(report.Users[1].Name);
        Assert.Equal(7, report.Users[0].Requests);
    }

    /// <summary>
    /// Verifies that the estimate treats local models as free and refuses to guess unknown ones.
    /// </summary>
    [Fact]
    public void Estimate_KnowsLocalModelsAreFree_AndRefusesToGuessUnknownOnes()
    {
        Assert.Equal(0.0, CostEstimator.Estimate("Ollama", "anything", 123456, 654321, null));
        Assert.Null(CostEstimator.Estimate("Anthropic", "claude-mystery-9", 1, 1, null));
        Assert.Equal(0.30, CostEstimator.Estimate("Anthropic", "claude-haiku-5-5", 2_000_000, 200_000, null)!.Value, 4);
    }

    private static MetricsSummary WithFailures(params (string Kind, int Count)[] failures)
        => Summary() with { FailuresByType = failures.Select(f => new CountBy(f.Kind, f.Count)).ToList() };

    /// <summary>
    /// Verifies that blocked requests are only the guardrail kinds, not other failures or timeouts.
    /// </summary>
    [Fact]
    public void BlockedRequests_AreOnlyTheGuardrailKinds_NotOtherFailures_AndNotTimeouts()
    {
        var summary = WithFailures(("rate_limited", 3), ("asks_paused", 2), ("crm_error", 5), ("timeout", 4), ("budget_exceeded", 1), ("question_too_long", 1));

        var report = AdminReportBuilder.Build(summary, From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(7, report.Blocked);
        Assert.Equal(new[] { "rate_limited", "asks_paused", "budget_exceeded", "question_too_long" }, report.BlockedByType!.Select(b => b.Key));
    }

    /// <summary>
    /// Verifies that no blocked requests gives zero and an empty list.
    /// </summary>
    [Fact]
    public void NoBlockedRequests_GivesZeroAndAnEmptyList()
    {
        var report = AdminReportBuilder.Build(WithFailures(("crm_error", 2)), From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(0, report.Blocked);
        Assert.Empty(report.BlockedByType!);
    }

    /// <summary>
    /// Verifies that knowledge rates are relative to the questions that searched and zero when none did.
    /// </summary>
    [Fact]
    public void KnowledgeRates_AreRelativeToTheQuestionsThatSearched_AndZeroWhenNoneDid()
    {
        var used = Summary() with { KnowledgeSearched = 8, KnowledgeUsed = 6, KnowledgePieces = 20 };

        var report = AdminReportBuilder.Build(used, From, To, null, new Dictionary<Guid, string>());
        var off = AdminReportBuilder.Build(Summary(), From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(0.75, report.KnowledgeUsedRate, 6);
        Assert.Equal(2.5, report.KnowledgeAveragePieces, 6);
        Assert.Equal(0, off.KnowledgeSearched);
        Assert.Equal(0, off.KnowledgeUsedRate);
        Assert.Equal(0, off.KnowledgeAveragePieces);
    }

    /// <summary>
    /// Verifies that the follow-up rate is relative to the answered questions and zero when none were answered.
    /// </summary>
    [Fact]
    public void FollowUpRate_IsRelativeToTheAnsweredQuestions_AndZeroWhenNoneWereAnswered()
    {
        var some = Summary() with { Answers = 8, FollowUps = 2 };
        var none = Summary() with { Answers = 0, FollowUps = 0 };

        var report = AdminReportBuilder.Build(some, From, To, null, new Dictionary<Guid, string>());
        var empty = AdminReportBuilder.Build(none, From, To, null, new Dictionary<Guid, string>());

        Assert.Equal(2, report.FollowUps);
        Assert.Equal(0.25, report.FollowUpRate, 6);
        Assert.Equal(0, empty.FollowUpRate);
    }
}
