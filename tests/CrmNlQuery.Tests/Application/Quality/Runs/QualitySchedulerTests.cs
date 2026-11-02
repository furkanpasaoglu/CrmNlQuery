using CrmNlQuery.Application.Quality.Runs;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Quality;

namespace CrmNlQuery.Tests.Application.Quality.Runs;

/// <summary>
/// Tests when the scheduled quality run starts, including hour, window, once per day, restart and reporting rules.
/// </summary>
public class QualitySchedulerTests
{
    private const string Open = "<fetch><entity name='incident'><attribute name='title'/><order attribute='title'/></entity></fetch>";
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    private sealed class Schema : IMetadataSource
    {
        private readonly EntityInfo _incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo>
            {
                new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
            }, new List<RelationshipInfo>());

        /// <summary>
        /// Returns the test schema's entity list.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary> { new EntitySummary("incident", "incidents", "Case") });

        /// <summary>
        /// Returns the test schema's entity definition, or null when unknown.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => Task.FromResult<EntityInfo?>(logicalName == "incident" ? _incident : null);

        /// <summary>
        /// Does nothing because the fake metadata never changes.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Executor : IQueryExecutor
    {
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(new ResultTable(new[] { "Case Title" }, new[] { "A", "B" }.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()));
    }

    private sealed class Runner : IQualityCaseRunner
    {
        public Func<CancellationToken, Task>? Delay;
        public Caller? LastCaller;

        /// <summary>
        /// Returns a scripted candidate run for a quality case in the test.
        /// </summary>
        public async Task<QualityCandidateRun> RunAsync(QualityCase item, Caller caller, CancellationToken ct = default)
        {
            LastCaller = caller;
            if (Delay is not null) { await Delay(ct); }
            var rows = item.Question == "fail" ? new[] { "X" } : new[] { "A", "B" };
            return new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, rows.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 100);
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new List<(LogLevel, string)>();
        /// <summary>
        /// Fake logger scope that does nothing.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Reports every log level as enabled.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>
        /// Stores the log message in a list for assertions.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Harness
    {
        public readonly MemoryQualityStore Cases = new MemoryQualityStore();
        public readonly MemoryQualityRunStore Runs = new MemoryQualityRunStore();
        public readonly Runner Runner = new Runner();
        public readonly ListLogger Log = new ListLogger();
        public QualitySchedule Schedule = new QualitySchedule(true, 3, 1, @"LAB\boss");
        public DateTimeOffset LocalNow = new DateTimeOffset(2026, 10, 27, 3, 5, 0, Offset);
        public Func<string, CancellationToken, Task<CallerResult>> Resolve = (account, ct) => Task.FromResult(new CallerResult(Caller.ForEvaluation(), null));
        public QualityRunService Service;
        public QualityScheduler Scheduler;

        public Harness()
        {
            Service = new QualityRunService(Cases, Runs, Runner, new Executor(), new Schema(), () => TimeSpan.FromMinutes(5), (a, b, c, d, ct) => Task.CompletedTask, clock: () => LocalNow.ToUniversalTime());
            Scheduler = NewScheduler();
        }

        /// <summary>
        /// Builds a scheduler wired to the test fakes.
        /// </summary>
        public QualityScheduler NewScheduler() => new QualityScheduler(Service, Runs, () => Schedule, (account, ct) => Resolve(account, ct), Log, () => LocalNow);

        /// <summary>
        /// Adds an active acceptance case for the test.
        /// </summary>
        public async Task AddCase(string question)
            => await Cases.AddAsync(new QualityCase
            {
                CreatedBy = "boss", CreatedAt = LocalNow, Source = QualityCase.SourceMarked, Verified = true, Status = QualityCase.StatusActive,
                Question = question, Entity = "incident", ReferenceFetch = Open,
            });

        /// <summary>
        /// Polls until at least one run exists and none is still active, failing with a timeout if background quality runs never finish.
        /// </summary>
        public async Task WaitForRunsToFinish()
        {
            for (var i = 0; i < 400; i++)
            {
                var runs = await Runs.ListRunsAsync(10);
                if (runs.Count > 0 && runs.All(r => !QualityRunStatus.IsActive(r.Status))) { return; }
                await Task.Delay(10);
            }
            throw new TimeoutException("the run did not finish");
        }
    }

    /// <summary>
    /// Verifies that a never enabled schedule does nothing.
    /// </summary>
    [Fact]
    public async Task ANeverEnabledScheduleDoesNothing()
    {
        var h = new Harness();
        await h.AddCase("good");
        h.Schedule = h.Schedule with { Enabled = false };

        Assert.Equal(ScheduleTick.Disabled, await h.Scheduler.TickAsync());
        Assert.Empty(await h.Runs.ListRunsAsync(10));
    }

    /// <summary>
    /// Verifies that it starts only inside the window after the configured hour.
    /// </summary>
    [Theory]
    [InlineData(2, 59, ScheduleTick.NotDue)]
    [InlineData(3, 0, ScheduleTick.Started)]
    [InlineData(5, 59, ScheduleTick.Started)]
    [InlineData(6, 0, ScheduleTick.NotDue)]
    [InlineData(14, 0, ScheduleTick.NotDue)]
    public async Task ItStartsOnlyInsideTheWindowAfterTheConfiguredHour(int hour, int minute, ScheduleTick expected)
    {
        var h = new Harness();
        await h.AddCase("good");
        h.LocalNow = new DateTimeOffset(2026, 10, 27, hour, minute, 0, Offset);

        Assert.Equal(expected, await h.Scheduler.TickAsync());
    }

    /// <summary>
    /// Verifies that the run belongs to the administrator who saved the schedule.
    /// </summary>
    [Fact]
    public async Task TheRunBelongsToTheAdministratorWhoSavedTheSchedule()
    {
        var h = new Harness();
        await h.AddCase("good");
        h.Schedule = new QualitySchedule(true, 3, 2, @"LAB\boss");

        Assert.Equal(ScheduleTick.Started, await h.Scheduler.TickAsync());
        await h.WaitForRunsToFinish();

        var run = Assert.Single(await h.Runs.ListRunsAsync(10));
        Assert.Equal(@"LAB\boss (scheduled)", run.StartedBy);
        Assert.True(QualityScheduler.IsScheduled(run));
        Assert.Equal(2, run.Repeat);
        Assert.NotNull(h.Runner.LastCaller);
    }

    /// <summary>
    /// Verifies that only one scheduled run starts per day even after a restart.
    /// </summary>
    [Fact]
    public async Task OnlyOneScheduledRunStartsPerDay_EvenAfterARestart()
    {
        var h = new Harness();
        await h.AddCase("good");

        Assert.Equal(ScheduleTick.Started, await h.Scheduler.TickAsync());
        await h.WaitForRunsToFinish();
        h.LocalNow = h.LocalNow.AddMinutes(30);
        Assert.Equal(ScheduleTick.AlreadyToday, await h.Scheduler.TickAsync());

        Assert.Equal(ScheduleTick.AlreadyToday, await h.NewScheduler().TickAsync());
        Assert.Single(await h.Runs.ListRunsAsync(10));
    }

    /// <summary>
    /// Verifies that the next day starts a new run.
    /// </summary>
    [Fact]
    public async Task TheNextDayStartsANewRun()
    {
        var h = new Harness();
        await h.AddCase("good");
        Assert.Equal(ScheduleTick.Started, await h.Scheduler.TickAsync());
        await h.WaitForRunsToFinish();

        h.LocalNow = h.LocalNow.AddDays(1);

        Assert.Equal(ScheduleTick.Started, await h.Scheduler.TickAsync());
        await h.WaitForRunsToFinish();
        Assert.Equal(2, (await h.Runs.ListRunsAsync(10)).Count);
    }

    /// <summary>
    /// Verifies that a manual run in progress makes it wait instead of failing.
    /// </summary>
    [Fact]
    public async Task AManualRunInProgressMakesItWaitInsteadOfFailing()
    {
        var h = new Harness();
        await h.AddCase("good");
        var gate = new TaskCompletionSource();
        h.Runner.Delay = _ => gate.Task;
        await h.Service.StartAsync(@"LAB\someone", Caller.ForEvaluation(), 1);

        Assert.Equal(ScheduleTick.Busy, await h.Scheduler.TickAsync());

        gate.SetResult();
        await h.WaitForRunsToFinish();
        Assert.Equal(ScheduleTick.Started, await h.Scheduler.TickAsync());
        await h.WaitForRunsToFinish();
    }

    /// <summary>
    /// Verifies that without active cases nothing starts and it is not retried all day.
    /// </summary>
    [Fact]
    public async Task WithoutActiveCasesNothingStartsAndItIsNotRetriedAllDay()
    {
        var h = new Harness();

        Assert.Equal(ScheduleTick.Empty, await h.Scheduler.TickAsync());
        Assert.Equal(ScheduleTick.AlreadyToday, await h.Scheduler.TickAsync());
        Assert.Empty(await h.Runs.ListRunsAsync(10));
    }

    /// <summary>
    /// Verifies that missing or unusable owner does not start and warns once per day.
    /// </summary>
    [Fact]
    public async Task MissingOrUnusableOwnerDoesNotStartAndWarnsOncePerDay()
    {
        var h = new Harness();
        await h.AddCase("good");
        h.Schedule = h.Schedule with { RunAs = "" };

        Assert.Equal(ScheduleTick.NoOwner, await h.Scheduler.TickAsync());
        Assert.Equal(ScheduleTick.NoOwner, await h.Scheduler.TickAsync());
        Assert.Equal(1, h.Log.Lines.Count(l => l.Level == LogLevel.Warning));

        h.Schedule = h.Schedule with { RunAs = @"LAB\gone" };
        h.Resolve = (account, ct) => Task.FromResult(new CallerResult(null, "The account was not found in the CRM."));
        h.LocalNow = h.LocalNow.AddDays(1);

        Assert.Equal(ScheduleTick.CannotResolve, await h.Scheduler.TickAsync());
        Assert.Equal(2, h.Log.Lines.Count(l => l.Level == LogLevel.Warning));
        Assert.Contains(h.Log.Lines, l => l.Message.Contains(@"LAB\gone"));
        Assert.Empty(await h.Runs.ListRunsAsync(10));
    }

    /// <summary>
    /// Verifies that a CRM that cannot be reached is retried every minute but warns only once.
    /// </summary>
    [Fact]
    public async Task ACrmThatCannotBeReachedIsRetriedEveryMinuteButWarnsOnlyOnce()
    {
        var h = new Harness();
        await h.AddCase("good");
        var attempts = 0;
        h.Resolve = (account, ct) => { attempts++; throw new InvalidOperationException("down"); };

        Assert.Equal(ScheduleTick.CannotResolve, await h.Scheduler.TickAsync());
        Assert.Equal(ScheduleTick.CannotResolve, await h.Scheduler.TickAsync());
        Assert.Equal(2, attempts);
        Assert.Equal(1, h.Log.Lines.Count(l => l.Level == LogLevel.Warning));

        h.Resolve = (account, ct) => Task.FromResult(new CallerResult(Caller.ForEvaluation(), null));
        Assert.Equal(ScheduleTick.Started, await h.Scheduler.TickAsync());
        await h.WaitForRunsToFinish();
    }

    /// <summary>
    /// Verifies that the next run is today while the window is open and nothing ran otherwise tomorrow.
    /// </summary>
    [Fact]
    public void TheNextRunIsTodayWhileTheWindowIsOpenAndNothingRanOtherwiseTomorrow()
    {
        var scheduler = new Harness().Scheduler;
        var schedule = new QualitySchedule(true, 3, 1, @"LAB\boss");
        DateTimeOffset At(int hour) => new DateTimeOffset(2026, 10, 27, hour, 0, 0, Offset);

        Assert.Equal(At(3), scheduler.NextRun(At(1), schedule, ranToday: false));
        Assert.Equal(At(3), scheduler.NextRun(At(4), schedule, ranToday: false));
        Assert.Equal(At(3).AddDays(1), scheduler.NextRun(At(4), schedule, ranToday: true));
        Assert.Equal(At(3).AddDays(1), scheduler.NextRun(At(9), schedule, ranToday: false));
        Assert.Null(scheduler.NextRun(At(1), schedule with { Enabled = false }, ranToday: false));
    }

    /// <summary>
    /// Verifies that a finished scheduled run is reported once warning when something fails.
    /// </summary>
    [Fact]
    public async Task AFinishedScheduledRunIsReportedOnce_WarningWhenSomethingFails()
    {
        var h = new Harness();
        await h.AddCase("good");
        await h.AddCase("fail");

        await h.Scheduler.TickAsync();
        await h.WaitForRunsToFinish();
        await h.Scheduler.ReportFinishedAsync();
        await h.Scheduler.ReportFinishedAsync();

        var reports = h.Log.Lines.Where(l => l.Message.Contains("found problems")).ToList();
        Assert.Single(reports);
        Assert.Equal(LogLevel.Warning, reports[0].Level);
        var last = await h.Scheduler.LastRunAsync();
        Assert.Equal(QualityRunStatus.Done, last!.Status);
        Assert.Equal(50.0, last.AccuracyPercent);
    }

    /// <summary>
    /// Verifies that a clean scheduled run is reported as information.
    /// </summary>
    [Fact]
    public async Task ACleanScheduledRunIsReportedAsInformation()
    {
        var h = new Harness();
        await h.AddCase("good");

        await h.Scheduler.TickAsync();
        await h.WaitForRunsToFinish();
        await h.Scheduler.ReportFinishedAsync();

        Assert.Contains(h.Log.Lines, l => l.Level == LogLevel.Information && l.Message.Contains("passed"));
        Assert.DoesNotContain(h.Log.Lines, l => l.Level == LogLevel.Warning);
    }

    /// <summary>
    /// Verifies that manual runs are never treated as scheduled ones.
    /// </summary>
    [Fact]
    public async Task ManualRunsAreNeverTreatedAsScheduledOnes()
    {
        var h = new Harness();
        await h.AddCase("good");
        await h.Service.StartAsync(@"LAB\boss", Caller.ForEvaluation(), 1);
        await h.WaitForRunsToFinish();

        Assert.Null(await h.Scheduler.LastRunAsync());
        Assert.False(await h.Scheduler.RanTodayAsync(h.LocalNow));
    }
}
