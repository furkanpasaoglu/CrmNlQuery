using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;
using CrmNlQuery.Application.Common.Exceptions;
using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Tests.Application.Operations.Services;

/// <summary>
/// Tests the daily cloud token budget guard.
/// </summary>
public class CloudBudgetTests
{
    private sealed class Limits : ILimitsSource
    {
        public GuardrailLimits Current { get; set; } = new GuardrailLimits { CloudDailyTokenBudget = 1000 };
    }

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeCloud : ILlmClient
    {
        public int Calls { get; private set; }
        public LlmUsage? Usage { get; set; } = new LlmUsage(300, 100);

        /// <summary>
        /// Fake cloud model client that reports token usage.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
            => Task.FromResult("{}");

        /// <summary>
        /// Fake cloud model client returning plain text with token usage.
        /// </summary>
        public Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new LlmResult("{}", Usage));
        }
    }

    private readonly Limits _limits = new Limits();
    private readonly Clock _clock = new Clock();
    private readonly FakeCloud _cloud = new FakeCloud();

    private (BudgetedLlmClient Client, CloudBudget Budget) Make()
    {
        var budget = new CloudBudget(() => _clock.Now);
        return (new BudgetedLlmClient(_cloud, _limits, budget), budget);
    }

    /// <summary>
    /// Verifies that each call adds its tokens to today's counter.
    /// </summary>
    [Fact]
    public async Task EachCallAddsItsTokensToTodaysCounter()
    {
        var (client, budget) = Make();

        await client.CompleteAsync("s", "u", "{}");
        await client.CompleteAsync("s", "u", "{}");

        Assert.Equal(800, budget.TokensToday);
    }

    /// <summary>
    /// Verifies that once the budget is reached the model is not called any more.
    /// </summary>
    [Fact]
    public async Task OnceTheBudgetIsReached_TheModelIsNotCalledAnyMore()
    {
        var (client, _) = Make();
        await client.CompleteAsync("s", "u", "{}");
        await client.CompleteAsync("s", "u", "{}");
        await client.CompleteAsync("s", "u", "{}");
        var calls = _cloud.Calls;

        await Assert.ThrowsAsync<BudgetExceededException>(() => client.CompleteAsync("s", "u", "{}"));

        Assert.Equal(calls, _cloud.Calls);
    }

    /// <summary>
    /// Verifies that a budget of zero means unlimited.
    /// </summary>
    [Fact]
    public async Task ABudgetOfZero_MeansUnlimited()
    {
        _limits.Current = new GuardrailLimits { CloudDailyTokenBudget = 0 };
        var (client, budget) = Make();
        budget.Seed(999_999_999);

        await client.CompleteAsync("s", "u", "{}");

        Assert.Equal(1, _cloud.Calls);
    }

    /// <summary>
    /// Verifies that the counter resets at midnight UTC.
    /// </summary>
    [Fact]
    public async Task TheCounterResetsAtMidnightUtc()
    {
        var (client, budget) = Make();
        budget.Seed(5000);
        await Assert.ThrowsAsync<BudgetExceededException>(() => client.CompleteAsync("s", "u", "{}"));

        _clock.Now = new DateTimeOffset(2026, 10, 19, 0, 0, 1, TimeSpan.Zero);

        await client.CompleteAsync("s", "u", "{}");
        Assert.Equal(400, budget.TokensToday);
    }

    /// <summary>
    /// Verifies that seeding at startup continues from what was already spent today.
    /// </summary>
    [Fact]
    public void SeedingAtStartup_ContinuesFromWhatWasAlreadySpentToday()
    {
        var (_, budget) = Make();

        budget.Seed(900);
        budget.Add(50);

        Assert.Equal(950, budget.TokensToday);
        Assert.False(budget.IsExceeded(1000));
        Assert.True(budget.IsExceeded(950));
        Assert.False(budget.IsExceeded(0));
    }

    /// <summary>
    /// Verifies that a provider that does not report tokens leaves the counter alone.
    /// </summary>
    [Fact]
    public async Task AProviderThatDoesNotReportTokens_LeavesTheCounterAlone()
    {
        _cloud.Usage = null;
        var (client, budget) = Make();

        await client.CompleteAsync("s", "u", "{}");

        Assert.Equal(0, budget.TokensToday);
    }

    /// <summary>
    /// Verifies that the limit is read on every call so a panel change applies immediately.
    /// </summary>
    [Fact]
    public async Task TheLimitIsReadOnEveryCall_SoAPanelChangeAppliesImmediately()
    {
        var (client, budget) = Make();
        budget.Seed(900);
        await client.CompleteAsync("s", "u", "{}");
        await Assert.ThrowsAsync<BudgetExceededException>(() => client.CompleteAsync("s", "u", "{}"));

        _limits.Current = new GuardrailLimits { CloudDailyTokenBudget = 5000 };

        await client.CompleteAsync("s", "u", "{}");
    }

    /// <summary>
    /// Verifies that a budget refusal is not recorded as a model error.
    /// </summary>
    [Fact]
    public async Task ABudgetRefusal_IsNotRecordedAsAModelError()
    {
        var metrics = new MemoryMetricsStore();
        var budget = new CloudBudget(() => _clock.Now);
        budget.Seed(5000);
        var budgeted = new BudgetedLlmClient(_cloud, _limits, budget);
        var metered = new MeteredLlmClient(budgeted, metrics, "plan", "anthropic", "claude-haiku-5-5");

        await Assert.ThrowsAsync<BudgetExceededException>(() => metered.CompleteAsync("s", "u", "{}"));

        var summary = await metrics.GetSummaryAsync(_clock.Now.AddDays(-1), _clock.Now.AddDays(1));
        Assert.Equal(0, summary.AiCalls);
        Assert.Equal(0, summary.AiErrors);
    }
}
