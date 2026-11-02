using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Tests.Contracts.Observability;

/// <summary>
/// Shared contract tests that every metrics store implementation, in-memory or PostgreSQL, must pass.
/// </summary>
public abstract class MetricsStoreContract : IAsyncLifetime
{
    protected abstract IMetricsStore CreateStore();

    private static readonly Random Rng = new Random();

    private static readonly DateTimeOffset TestEra = new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset NewDay() => new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Rng.Next(0, 18000));

    /// <summary>
    /// Prepares the shared metrics-store contract tests before each test run.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Cleans up the metrics-store contract test data after each run.
    /// </summary>
    public async Task DisposeAsync() => await CreateStore().PurgeOlderThanAsync(TestEra);

    private static AiCallRecord Call(DateTimeOffset at, string purpose = "plan", string model = "m-test", long ms = 100,
        int? input = 10, int? output = 5, string outcome = "ok", string? error = null, Guid? request = null,
        int? promptChars = null, int? estTokens = null)
        => new AiCallRecord(request ?? Guid.NewGuid(), at, purpose, "TestProvider", model, ms, input, output, outcome, error, promptChars, estTokens);

    private static RequestRecord Req(DateTimeOffset at, string outcome = "answer", string? error = null, Guid? user = null, long ms = 1000)
        => new RequestRecord(Guid.NewGuid(), at, user, ms, outcome, error);

    private static Task<MetricsSummary> Summary(IMetricsStore store, DateTimeOffset day)
        => store.GetSummaryAsync(day, day.AddDays(1));

    private static RequestRecord ReqWithKnowledge(DateTimeOffset at, int? pieces)
        => new RequestRecord(Guid.NewGuid(), at, null, 1000, "answer", null, pieces);

    private static RequestRecord ReqFollow(DateTimeOffset at, bool? followUp, string outcome = "answer")
        => new RequestRecord(Guid.NewGuid(), at, null, 1000, outcome, null, null, followUp);

    /// <summary>
    /// Verifies that cancelled asks are counted separately not as failures but still as requests.
    /// </summary>
    [Fact]
    public async Task CancelledAsks_AreCountedSeparately_NotAsFailures_ButStillAsRequests()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(Req(day.AddHours(1)));
        await store.RecordRequestAsync(Req(day.AddHours(2), "failure", "timeout"));
        await store.RecordRequestAsync(Req(day.AddHours(3), "cancelled", "plan"));
        await store.RecordRequestAsync(Req(day.AddHours(4), "cancelled", "step2:crm"));

        var s = await Summary(store, day);

        Assert.Equal(4, s.Requests);
        Assert.Equal(1, s.Failures);
        Assert.Equal(2, s.Cancelled);
        Assert.Single(s.FailuresByType);
        Assert.Equal("timeout", s.FailuresByType[0].Key);
    }

    /// <summary>
    /// Verifies that follow ups are counted among the answers not among questions or failures.
    /// </summary>
    [Fact]
    public async Task FollowUps_AreCountedAmongTheAnswers_NotAmongQuestionsOrFailures()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(ReqFollow(day.AddHours(1), true));
        await store.RecordRequestAsync(ReqFollow(day.AddHours(2), true));
        await store.RecordRequestAsync(ReqFollow(day.AddHours(3), false));
        await store.RecordRequestAsync(ReqFollow(day.AddHours(4), null, "question"));
        await store.RecordRequestAsync(ReqFollow(day.AddHours(5), null, "failure"));

        var s = await Summary(store, day);

        Assert.Equal(3, s.Answers);
        Assert.Equal(2, s.FollowUps);
    }

    /// <summary>
    /// Verifies that knowledge usage counts searched questions those that found something and the pieces.
    /// </summary>
    [Fact]
    public async Task KnowledgeUsage_CountsSearchedQuestions_ThoseThatFoundSomething_AndThePieces()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(ReqWithKnowledge(day.AddHours(1), 4));
        await store.RecordRequestAsync(ReqWithKnowledge(day.AddHours(2), 2));
        await store.RecordRequestAsync(ReqWithKnowledge(day.AddHours(3), 0));
        await store.RecordRequestAsync(ReqWithKnowledge(day.AddHours(4), null));

        var s = await Summary(store, day);

        Assert.Equal(4, s.Requests);
        Assert.Equal(3, s.KnowledgeSearched);
        Assert.Equal(2, s.KnowledgeUsed);
        Assert.Equal(6, s.KnowledgePieces);
    }

    /// <summary>
    /// Verifies that when the knowledge base was never searched the knowledge numbers are zero.
    /// </summary>
    [Fact]
    public async Task WhenTheKnowledgeBaseWasNeverSearched_TheKnowledgeNumbersAreZero()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(Req(day.AddHours(1)));

        var s = await Summary(store, day);

        Assert.Equal(0, s.KnowledgeSearched);
        Assert.Equal(0, s.KnowledgeUsed);
        Assert.Equal(0, s.KnowledgePieces);
    }

    /// <summary>
    /// Verifies that knowledge usage is counted only inside the window.
    /// </summary>
    [Fact]
    public async Task KnowledgeUsage_IsCountedOnlyInsideTheWindow()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(ReqWithKnowledge(day.AddHours(1), 3));
        await store.RecordRequestAsync(ReqWithKnowledge(day.AddDays(1).AddHours(1), 9));

        var s = await Summary(store, day);

        Assert.Equal(1, s.KnowledgeSearched);
        Assert.Equal(3, s.KnowledgePieces);
    }

    /// <summary>
    /// Verifies that totals count requests by outcome and ai calls.
    /// </summary>
    [Fact]
    public async Task Totals_CountRequestsByOutcome_AndAiCalls()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(Req(day.AddHours(1), "answer"));
        await store.RecordRequestAsync(Req(day.AddHours(2), "answer"));
        await store.RecordRequestAsync(Req(day.AddHours(3), "question"));
        await store.RecordRequestAsync(Req(day.AddHours(4), "failure", "crm_error"));
        await store.RecordAiCallAsync(Call(day.AddHours(1), input: 100, output: 20));
        await store.RecordAiCallAsync(Call(day.AddHours(2), input: 50, output: 10, outcome: "error", error: "unavailable"));

        var s = await Summary(store, day);

        Assert.Equal(4, s.Requests);
        Assert.Equal(2, s.Answers);
        Assert.Equal(1, s.Questions);
        Assert.Equal(1, s.Failures);
        Assert.Equal(2, s.AiCalls);
        Assert.Equal(1, s.AiErrors);
        Assert.Equal(150, s.InputTokens);
        Assert.Equal(30, s.OutputTokens);
    }

    /// <summary>
    /// Verifies that unknown tokens are counted as zero but the call still counts.
    /// </summary>
    [Fact]
    public async Task UnknownTokens_AreCountedAsZero_ButTheCallStillCounts()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordAiCallAsync(Call(day.AddHours(1), input: null, output: null));
        await store.RecordAiCallAsync(Call(day.AddHours(2), input: 7, output: 3));
        var s = await Summary(store, day);
        Assert.Equal(2, s.AiCalls);
        Assert.Equal(7, s.InputTokens);
        Assert.Equal(3, s.OutputTokens);
    }

    /// <summary>
    /// Verifies that summary only covers the asked window.
    /// </summary>
    [Fact]
    public async Task Summary_OnlyCoversTheAskedWindow()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(Req(day.AddHours(5)));
        await store.RecordRequestAsync(Req(day.AddDays(-1).AddHours(5)));
        await store.RecordRequestAsync(Req(day.AddDays(1).AddHours(5)));
        await store.RecordAiCallAsync(Call(day.AddDays(1).AddHours(1)));

        var s = await Summary(store, day);

        Assert.Equal(1, s.Requests);
        Assert.Equal(0, s.AiCalls);
    }

    /// <summary>
    /// Verifies that days are grouped by UTC date.
    /// </summary>
    [Fact]
    public async Task Days_AreGroupedByUtcDate()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(Req(day.AddHours(1)));
        await store.RecordRequestAsync(Req(day.AddDays(1).AddHours(1), "failure", "crm_error"));
        await store.RecordRequestAsync(Req(day.AddDays(1).AddHours(2)));
        await store.RecordAiCallAsync(Call(day.AddDays(1).AddHours(3), input: 40, output: 4));

        var s = await store.GetSummaryAsync(day, day.AddDays(2));

        Assert.Equal(2, s.Days.Count);
        var first = s.Days.Single(d => d.Day == DateOnly.FromDateTime(day.UtcDateTime));
        var second = s.Days.Single(d => d.Day == DateOnly.FromDateTime(day.AddDays(1).UtcDateTime));
        Assert.Equal(1, first.Requests);
        Assert.Equal(2, second.Requests);
        Assert.Equal(1, second.Failures);
        Assert.Equal(1, second.AiCalls);
        Assert.Equal(40, second.InputTokens);
    }

    /// <summary>
    /// Verifies that latency has percentiles per purpose with linear interpolation.
    /// </summary>
    [Fact]
    public async Task Latency_HasPercentilesPerPurpose_WithLinearInterpolation()
    {
        var store = CreateStore();
        var day = NewDay();
        foreach (var ms in new long[] { 100, 200, 300, 400, 1000 })
        {
            await store.RecordAiCallAsync(Call(day.AddHours(1), purpose: "plan", ms: ms));
        }
        await store.RecordAiCallAsync(Call(day.AddHours(2), purpose: "analyze", ms: 50, outcome: "error", error: "unavailable"));

        var s = await Summary(store, day);

        var plan = s.Latency.Single(l => l.Purpose == "plan");
        Assert.Equal(5, plan.Calls);
        Assert.Equal(0, plan.Errors);
        Assert.Equal(300, plan.P50Ms, 1);
        Assert.Equal(880, plan.P95Ms, 1);
        var analyze = s.Latency.Single(l => l.Purpose == "analyze");
        Assert.Equal(1, analyze.Errors);
    }

    /// <summary>
    /// Verifies that latency has the average prompt size of the calls that reported it.
    /// </summary>
    [Fact]
    public async Task Latency_HasTheAveragePromptSize_OfTheCallsThatReportedIt()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordAiCallAsync(Call(day.AddHours(1), purpose: "plan", promptChars: 7000, estTokens: 2000));
        await store.RecordAiCallAsync(Call(day.AddHours(2), purpose: "plan", promptChars: 3500, estTokens: 1000));
        await store.RecordAiCallAsync(Call(day.AddHours(3), purpose: "plan"));
        await store.RecordAiCallAsync(Call(day.AddHours(4), purpose: "analyze"));

        var s = await Summary(store, day);

        var plan = s.Latency.Single(l => l.Purpose == "plan");
        Assert.Equal(5250, plan.AvgPromptChars!.Value, 1);
        Assert.Equal(1500, plan.AvgEstTokens!.Value, 1);
        var analyze = s.Latency.Single(l => l.Purpose == "analyze");
        Assert.Null(analyze.AvgPromptChars);
        Assert.Null(analyze.AvgEstTokens);
    }

    /// <summary>
    /// Verifies that request latency has percentiles.
    /// </summary>
    [Fact]
    public async Task RequestLatency_HasPercentiles()
    {
        var store = CreateStore();
        var day = NewDay();
        foreach (var ms in new long[] { 1000, 2000, 3000, 4000, 10000 })
        {
            await store.RecordRequestAsync(Req(day.AddHours(1), ms: ms));
        }
        var s = await Summary(store, day);
        Assert.Equal(3000, s.RequestP50Ms, 1);
        Assert.Equal(8800, s.RequestP95Ms, 1);
    }

    /// <summary>
    /// Verifies that failures and ai errors are grouped by type most frequent first.
    /// </summary>
    [Fact]
    public async Task Failures_AndAiErrors_AreGroupedByType_MostFrequentFirst()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordRequestAsync(Req(day.AddHours(1), "failure", "crm_error"));
        await store.RecordRequestAsync(Req(day.AddHours(2), "failure", "llm_unavailable"));
        await store.RecordRequestAsync(Req(day.AddHours(3), "failure", "llm_unavailable"));
        await store.RecordAiCallAsync(Call(day.AddHours(1), outcome: "error", error: "unavailable"));
        await store.RecordAiCallAsync(Call(day.AddHours(2), outcome: "error", error: "unavailable"));
        await store.RecordAiCallAsync(Call(day.AddHours(3), outcome: "error", error: "other"));

        var s = await Summary(store, day);

        Assert.Equal(new[] { "llm_unavailable", "crm_error" }, s.FailuresByType.Select(f => f.Key).ToArray());
        Assert.Equal(2, s.FailuresByType[0].Count);
        Assert.Equal(new[] { "unavailable", "other" }, s.AiErrorsByType.Select(f => f.Key).ToArray());
    }

    /// <summary>
    /// Verifies that models sum tokens per provider and model.
    /// </summary>
    [Fact]
    public async Task Models_SumTokensPerProviderAndModel()
    {
        var store = CreateStore();
        var day = NewDay();
        await store.RecordAiCallAsync(Call(day.AddHours(1), model: "alpha", input: 100, output: 10));
        await store.RecordAiCallAsync(Call(day.AddHours(2), model: "alpha", input: 200, output: 20));
        await store.RecordAiCallAsync(Call(day.AddHours(3), model: "beta", input: 5, output: 1));

        var s = await Summary(store, day);

        var alpha = s.Models.Single(m => m.Model == "alpha");
        Assert.Equal(2, alpha.Calls);
        Assert.Equal(300, alpha.InputTokens);
        Assert.Equal(30, alpha.OutputTokens);
        Assert.Equal(1, s.Models.Single(m => m.Model == "beta").Calls);
    }

    /// <summary>
    /// Verifies that top users are ranked by requests and skip unknown users.
    /// </summary>
    [Fact]
    public async Task TopUsers_AreRankedByRequests_AndSkipUnknownUsers()
    {
        var store = CreateStore();
        var day = NewDay();
        var busy = Guid.NewGuid();
        var quiet = Guid.NewGuid();
        await store.RecordRequestAsync(Req(day.AddHours(1), user: busy));
        await store.RecordRequestAsync(Req(day.AddHours(2), user: busy));
        await store.RecordRequestAsync(Req(day.AddHours(3), user: quiet));
        await store.RecordRequestAsync(Req(day.AddHours(4), user: null));

        var s = await Summary(store, day);

        Assert.Equal(new[] { busy, quiet }, s.TopUsers.Select(u => u.User).ToArray());
        Assert.Equal(2, s.TopUsers[0].Requests);
    }

    /// <summary>
    /// Verifies that purge removes only older records and tells how many.
    /// </summary>
    [Fact]
    public async Task Purge_RemovesOnlyOlderRecords_AndTellsHowMany()
    {
        var store = CreateStore();
        var old = NewDay();
        await store.RecordRequestAsync(Req(old));
        await store.RecordAiCallAsync(Call(old));
        await store.RecordAiCallAsync(Call(old.AddHours(1)));

        var removed = await store.PurgeOlderThanAsync(old.AddDays(1));

        Assert.True(removed >= 3);
        var s = await store.GetSummaryAsync(old, old.AddDays(1));
        Assert.Equal(0, s.Requests);
        Assert.Equal(0, s.AiCalls);
    }

    /// <summary>
    /// Verifies that empty window gives an empty summary not an error.
    /// </summary>
    [Fact]
    public async Task EmptyWindow_GivesAnEmptySummary_NotAnError()
    {
        var store = CreateStore();
        var s = await Summary(store, NewDay());
        Assert.Equal(0, s.Requests);
        Assert.Equal(0, s.AiCalls);
        Assert.Empty(s.Days);
        Assert.Empty(s.Latency);
        Assert.Equal(0, s.RequestP50Ms);
    }
}
