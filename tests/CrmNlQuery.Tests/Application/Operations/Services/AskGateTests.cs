using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Application.Operations.Services;

namespace CrmNlQuery.Tests.Application.Operations.Services;

/// <summary>
/// Tests the ask gate: per-user quotas, concurrency limits and the emergency stop.
/// </summary>
public class AskGateTests
{
    private sealed class MutableLimits : ILimitsSource
    {
        public GuardrailLimits Current { get; set; } = new GuardrailLimits();
    }

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private readonly MutableLimits _limits = new MutableLimits();
    private readonly Clock _clock = new Clock();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    private AskGate Gate(int maxUsers = AskGate.DefaultMaxUsers) => new AskGate(_limits, () => _clock.Now, maxUsers);

    private static GateDenial Denied(GateDecision d) => Assert.IsType<GateDenial>(d.Denial);

    /// <summary>
    /// Verifies that a first question is allowed and counts as running until the lease is released.
    /// </summary>
    [Fact]
    public void AFirstQuestion_IsAllowed_AndCountsAsRunningUntilTheLeaseIsReleased()
    {
        var gate = Gate();

        var decision = gate.TryEnter(_alice);

        Assert.True(decision.Allowed);
        Assert.Equal(1, gate.RunningTotal);
        decision.Lease!.Dispose();
        Assert.Equal(0, gate.RunningTotal);
    }

    /// <summary>
    /// Verifies that disposing a lease twice releases only once.
    /// </summary>
    [Fact]
    public void DisposingALeaseTwice_ReleasesOnlyOnce()
    {
        var gate = Gate();
        var first = gate.TryEnter(_alice).Lease!;
        var second = gate.TryEnter(_bob).Lease!;

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, gate.RunningTotal);
        second.Dispose();
    }

    /// <summary>
    /// Verifies that when paused every question is refused with503 and a retry hint.
    /// </summary>
    [Fact]
    public void WhenPaused_EveryQuestionIsRefused_With503AndARetryHint()
    {
        _limits.Current = new GuardrailLimits { AskEnabled = false };

        var denial = Denied(Gate().TryEnter(_alice));

        Assert.Equal(GateDenial.AsksPaused, denial.Kind);
        Assert.Equal(503, denial.HttpStatus);
        Assert.True(denial.RetryAfterSeconds > 0);
        Assert.Contains("paused", denial.Message);
    }

    /// <summary>
    /// Verifies that resuming takes effect on the next question.
    /// </summary>
    [Fact]
    public void ResumingTakesEffectOnTheNextQuestion()
    {
        var gate = Gate();
        _limits.Current = new GuardrailLimits { AskEnabled = false };
        Assert.False(gate.TryEnter(_alice).Allowed);

        _limits.Current = new GuardrailLimits { AskEnabled = true };

        Assert.True(gate.TryEnter(_alice).Allowed);
    }

    /// <summary>
    /// Verifies that the per minute limit refuses the next question with429 and how long to wait.
    /// </summary>
    [Fact]
    public void ThePerMinuteLimit_RefusesTheNextQuestion_With429_AndHowLongToWait()
    {
        _limits.Current = new GuardrailLimits { QuestionsPerMinute = 3 };
        var gate = Gate();
        for (var i = 0; i < 3; i++)
        {
            gate.TryEnter(_alice).Lease!.Dispose();
            _clock.Now = _clock.Now.AddSeconds(5);
        }

        var denial = Denied(gate.TryEnter(_alice));

        Assert.Equal(GateDenial.RateLimited, denial.Kind);
        Assert.Equal(429, denial.HttpStatus);
        Assert.InRange(denial.RetryAfterSeconds, 40, 60);
    }

    /// <summary>
    /// Verifies that the minute window slides so a question is allowed again when the oldest expires.
    /// </summary>
    [Fact]
    public void TheMinuteWindowSlides_SoAQuestionIsAllowedAgainWhenTheOldestExpires()
    {
        _limits.Current = new GuardrailLimits { QuestionsPerMinute = 2 };
        var gate = Gate();
        gate.TryEnter(_alice).Lease!.Dispose();
        _clock.Now = _clock.Now.AddSeconds(30);
        gate.TryEnter(_alice).Lease!.Dispose();
        Assert.False(gate.TryEnter(_alice).Allowed);

        _clock.Now = _clock.Now.AddSeconds(31);

        Assert.True(gate.TryEnter(_alice).Allowed);
    }

    /// <summary>
    /// Verifies that a refused question does not use up the quota.
    /// </summary>
    [Fact]
    public void ARefusedQuestion_DoesNotUseUpTheQuota()
    {
        _limits.Current = new GuardrailLimits { QuestionsPerMinute = 1 };
        var gate = Gate();
        gate.TryEnter(_alice).Lease!.Dispose();
        for (var i = 0; i < 10; i++)
        {
            Assert.False(gate.TryEnter(_alice).Allowed);
        }

        _clock.Now = _clock.Now.AddSeconds(61);

        Assert.True(gate.TryEnter(_alice).Allowed);
    }

    /// <summary>
    /// Verifies that the daily limit refuses with the time until midnight UTC and resets at midnight.
    /// </summary>
    [Fact]
    public void TheDailyLimit_RefusesWithTheTimeUntilMidnightUtc_AndResetsAtMidnight()
    {
        _limits.Current = new GuardrailLimits { QuestionsPerDay = 2, QuestionsPerMinute = 100 };
        _clock.Now = new DateTimeOffset(2026, 10, 18, 23, 0, 0, TimeSpan.Zero);
        var gate = Gate();
        gate.TryEnter(_alice).Lease!.Dispose();
        gate.TryEnter(_alice).Lease!.Dispose();

        var denial = Denied(gate.TryEnter(_alice));

        Assert.Equal(GateDenial.DailyQuota, denial.Kind);
        Assert.Equal(429, denial.HttpStatus);
        Assert.Equal(3600, denial.RetryAfterSeconds);
        Assert.Contains("midnight UTC", denial.Message);

        _clock.Now = new DateTimeOffset(2026, 10, 19, 0, 0, 1, TimeSpan.Zero);

        Assert.True(gate.TryEnter(_alice).Allowed);
    }

    /// <summary>
    /// Verifies that the per user concurrency limit refuses a third parallel question and frees up when one finishes.
    /// </summary>
    [Fact]
    public void ThePerUserConcurrencyLimit_RefusesAThirdParallelQuestion_AndFreesUpWhenOneFinishes()
    {
        _limits.Current = new GuardrailLimits { MaxConcurrentPerUser = 2 };
        var gate = Gate();
        var a = gate.TryEnter(_alice).Lease!;
        gate.TryEnter(_alice);

        var denial = Denied(gate.TryEnter(_alice));

        Assert.Equal(GateDenial.BusyUser, denial.Kind);
        Assert.Equal(429, denial.HttpStatus);
        a.Dispose();
        Assert.True(gate.TryEnter(_alice).Allowed);
    }

    /// <summary>
    /// Verifies that the total concurrency limit protects the model server with503 and other users are affected only by it.
    /// </summary>
    [Fact]
    public void TheTotalConcurrencyLimit_ProtectsTheModelServer_With503_AndOtherUsersAreAffectedOnlyByIt()
    {
        _limits.Current = new GuardrailLimits { MaxConcurrentTotal = 2, MaxConcurrentPerUser = 5 };
        var gate = Gate();
        gate.TryEnter(_alice);
        gate.TryEnter(_alice);

        var denial = Denied(gate.TryEnter(_bob));

        Assert.Equal(GateDenial.BusyTotal, denial.Kind);
        Assert.Equal(503, denial.HttpStatus);
    }

    /// <summary>
    /// Verifies that a denial because the service is busy does not use up the users quota.
    /// </summary>
    [Fact]
    public void ADenialBecauseTheServiceIsBusy_DoesNotUseUpTheUsersQuota()
    {
        _limits.Current = new GuardrailLimits { MaxConcurrentTotal = 1, QuestionsPerDay = 1 };
        var gate = Gate();
        var busy = gate.TryEnter(_alice).Lease!;
        Assert.False(gate.TryEnter(_bob).Allowed);
        busy.Dispose();

        Assert.True(gate.TryEnter(_bob).Allowed);
    }

    /// <summary>
    /// Verifies that different users do not affect each others quotas.
    /// </summary>
    [Fact]
    public void DifferentUsers_DoNotAffectEachOthersQuotas()
    {
        _limits.Current = new GuardrailLimits { QuestionsPerMinute = 1, MaxConcurrentPerUser = 1 };
        var gate = Gate();
        gate.TryEnter(_alice).Lease!.Dispose();

        Assert.False(gate.TryEnter(_alice).Allowed);
        Assert.True(gate.TryEnter(_bob).Allowed);
    }

    /// <summary>
    /// Verifies that a new limit applies immediately without restarting the gate.
    /// </summary>
    [Fact]
    public void ANewLimit_AppliesImmediately_WithoutRestartingTheGate()
    {
        var gate = Gate();
        gate.TryEnter(_alice).Lease!.Dispose();
        _limits.Current = new GuardrailLimits { QuestionsPerMinute = 1 };

        Assert.False(gate.TryEnter(_alice).Allowed);

        _limits.Current = new GuardrailLimits { QuestionsPerMinute = 5 };

        Assert.True(gate.TryEnter(_alice).Allowed);
    }

    /// <summary>
    /// Verifies that many parallel callers never exceed the total limit and every lease is released.
    /// </summary>
    [Fact]
    public async Task ManyParallelCallers_NeverExceedTheTotalLimit_AndEveryLeaseIsReleased()
    {
        _limits.Current = new GuardrailLimits { MaxConcurrentTotal = 3, MaxConcurrentPerUser = 3, QuestionsPerMinute = 600, QuestionsPerDay = 100000 };
        var gate = Gate();
        var peak = 0;
        var current = 0;
        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            var decision = gate.TryEnter(Guid.NewGuid());
            if (!decision.Allowed) return;
            using (decision.Lease)
            {
                var now = Interlocked.Increment(ref current);
                int seen;
                while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
                await Task.Delay(2);
                Interlocked.Decrement(ref current);
            }
        }));

        await Task.WhenAll(tasks);

        Assert.True(peak <= 3, "peak " + peak);
        Assert.Equal(0, gate.RunningTotal);
    }

    /// <summary>
    /// Verifies that when the user table is full idle users from earlier days are dropped but todays are kept.
    /// </summary>
    [Fact]
    public void WhenTheUserTableIsFull_IdleUsersFromEarlierDaysAreDropped_ButTodaysAreKept()
    {
        var gate = Gate(maxUsers: 3);
        var old1 = Guid.NewGuid();
        var old2 = Guid.NewGuid();
        gate.TryEnter(old1).Lease!.Dispose();
        gate.TryEnter(old2).Lease!.Dispose();
        gate.TryEnter(_alice).Lease!.Dispose();
        _clock.Now = _clock.Now.AddDays(1);

        gate.TryEnter(_bob).Lease!.Dispose();

        Assert.Equal(1, gate.TrackedUsers);
    }

    /// <summary>
    /// Verifies that a running user is never dropped.
    /// </summary>
    [Fact]
    public void ARunningUser_IsNeverDropped()
    {
        var gate = Gate(maxUsers: 1);
        var running = gate.TryEnter(_alice).Lease!;
        _clock.Now = _clock.Now.AddDays(1);

        gate.TryEnter(_bob).Lease!.Dispose();

        Assert.Equal(2, gate.TrackedUsers);
        running.Dispose();
    }
}
