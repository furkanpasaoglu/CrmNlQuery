using CrmNlQuery.Application.Operations.Startup;

namespace CrmNlQuery.Tests.Application.Operations.Startup;

/// <summary>
/// Tests the startup retry that waits for a dependent service such as PostgreSQL.
/// </summary>
public class StartupRetryTests
{
    private sealed class FakeClock
    {
        public TimeSpan Now { get; private set; }
        /// <summary>
        /// Records the requested delay without waiting.
        /// </summary>
        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            Now += span;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Verifies that succeeds without, waiting, and when, and the, and first, and probe, and works.
    /// </summary>
    [Fact]
    public async Task Succeeds_without_waiting_when_the_first_probe_works()
    {
        var clock = new FakeClock();
        var calls = 0;

        await StartupRetry.WaitAsync("db", _ => { calls++; return Task.CompletedTask; }, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5),
            delay: clock.Delay, elapsed: () => clock.Now);

        Assert.Equal(1, calls);
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    /// <summary>
    /// Verifies that retries until, the, and service, and is, and up, and and, and reports, and each, and attempt.
    /// </summary>
    [Fact]
    public async Task Retries_until_the_service_is_up_and_reports_each_attempt()
    {
        var clock = new FakeClock();
        var calls = 0;
        var reported = new List<int>();

        await StartupRetry.WaitAsync("db", _ =>
            {
                calls++;
                return calls < 4 ? Task.FromException(new IOException("connection refused")) : Task.CompletedTask;
            },
            TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5),
            onRetry: (attempt, _, _) => reported.Add(attempt), delay: clock.Delay, elapsed: () => clock.Now);

        Assert.Equal(4, calls);
        Assert.Equal(new[] { 1, 2, 3 }, reported);
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Now);
    }

    /// <summary>
    /// Verifies that gives up, with, and a, and clear, and message, and after, and the, and maximum, and wait.
    /// </summary>
    [Fact]
    public async Task Gives_up_with_a_clear_message_after_the_maximum_wait()
    {
        var clock = new FakeClock();
        var calls = 0;

        var error = await Assert.ThrowsAsync<StartupDependencyException>(() => StartupRetry.WaitAsync("PostgreSQL",
            _ => { calls++; return Task.FromException(new IOException("connection refused")); },
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5), delay: clock.Delay, elapsed: () => clock.Now));

        Assert.True(error.CanRetry);
        Assert.Contains("PostgreSQL did not become reachable within 20 seconds", error.Message);
        Assert.Contains("connection refused", error.Message);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(5, calls);
    }

    /// <summary>
    /// Verifies that a zero, wait, and fails, and on, and the, and first, and error, and like, and before.
    /// </summary>
    [Fact]
    public async Task A_zero_wait_fails_on_the_first_error_like_before()
    {
        var calls = 0;

        await Assert.ThrowsAsync<StartupDependencyException>(() => StartupRetry.WaitAsync("db",
            _ => { calls++; return Task.FromException(new IOException("down")); },
            TimeSpan.Zero, TimeSpan.FromSeconds(5), delay: (_, _) => Task.CompletedTask, elapsed: () => TimeSpan.Zero));

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Verifies that a permanent, error, and is, and not, and retried.
    /// </summary>
    [Fact]
    public async Task A_permanent_error_is_not_retried()
    {
        var clock = new FakeClock();
        var calls = 0;

        var error = await Assert.ThrowsAsync<StartupDependencyException>(() => StartupRetry.WaitAsync("db",
            _ => { calls++; return Task.FromException(new UnauthorizedAccessException("password authentication failed")); },
            TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5), isTransient: ex => ex is not UnauthorizedAccessException,
            delay: clock.Delay, elapsed: () => clock.Now));

        Assert.Equal(1, calls);
        Assert.False(error.CanRetry);
        Assert.Contains("retrying will not help", error.Message);
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    /// <summary>
    /// Verifies that cancellation stops, the, and wait, and immediately.
    /// </summary>
    [Fact]
    public async Task Cancellation_stops_the_wait_immediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StartupRetry.WaitAsync("db",
            token => Task.FromCanceled(token), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5), ct: cts.Token));
    }
}
