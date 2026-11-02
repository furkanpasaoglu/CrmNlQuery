using CrmNlQuery.Application.Quality.Runs;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Quality;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Quality.Runs;

/// <summary>
/// Tests the quality check run covering verdicts, data drift, regression report, single active run, cancel and restart recovery.
/// </summary>
public class QualityRunServiceTests
{
    private const string Open = "<fetch><entity name='incident'><attribute name='title'/><order attribute='title'/></entity></fetch>";
    private const string Closed = "<fetch><entity name='incident'><attribute name='secretfield'/></entity></fetch>";

    private sealed class Schema : IMetadataSource
    {
        private readonly EntityInfo _incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo>
            {
                new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
            }, new List<RelationshipInfo>());

        /// <summary>
        /// Test fake that returns the list of entity summaries.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary> { new EntitySummary("incident", "incidents", "Case") });

        /// <summary>
        /// Test fake that returns entity metadata by logical name.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => Task.FromResult<EntityInfo?>(logicalName == "incident" ? _incident : null);

        /// <summary>
        /// Test fake metadata refresh that does nothing.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Executor : IQueryExecutor
    {
        public string[] Rows = { "A", "B", "C" };
        public bool Denied;
        public bool Broken;

        /// <summary>
        /// Test fake executor that returns a canned result table for a query.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            if (Denied) { throw new CrmException("Principal user is missing prvReadIncident", isPermissionDenied: true); }
            if (Broken) { throw new InvalidOperationException("boom"); }
            return Task.FromResult(new ResultTable(new[] { "Case Title" }, Rows.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()));
        }
    }

    private sealed class Runner : IQualityCaseRunner
    {
        public int Calls;
        public Func<QualityCase, int, QualityCandidateRun>? Script;
        public Func<CancellationToken, Task>? Delay;

        /// <summary>
        /// Test fake candidate runner that returns a canned candidate result for a quality case.
        /// </summary>
        public async Task<QualityCandidateRun> RunAsync(QualityCase item, Caller caller, CancellationToken ct = default)
        {
            var n = Calls++;
            if (Delay is not null) { await Delay(ct); }
            ct.ThrowIfCancellationRequested();
            if (Script is not null) { return Script(item, n); }
            return item.Question switch
            {
                "fail" => Answer("X", "Y"),
                "flaky" => n % 2 == 0 ? Answer("A", "B", "C") : Answer("A"),
                "question" => new QualityCandidateRun("failure", null, "asked a question", 100),
                "denied" => new QualityCandidateRun("unreadable", null, "not allowed", 100),
                _ => Answer("A", "B", "C"),
            };
        }

        private static QualityCandidateRun Answer(params string[] rows)
            => new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, rows.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 200);
    }

    private sealed class Harness
    {
        public readonly MemoryQualityStore Cases = new MemoryQualityStore();
        public readonly MemoryQualityRunStore Runs = new MemoryQualityRunStore();
        public readonly Executor Executor = new Executor();
        public readonly Runner Runner = new Runner();
        public readonly List<string> Audit = new List<string>();
        public TimeSpan Timeout = TimeSpan.FromMinutes(5);
        public DateTimeOffset Now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        public QualityRunService Service;

        public Harness()
        {
            Service = new QualityRunService(Cases, Runs, Runner, Executor, new Schema(), () => Timeout,
                (actor, action, target, detail, ct) => { Audit.Add(actor + "|" + action + "|" + target + "|" + detail); return Task.CompletedTask; }, clock: () => Now);
        }

        /// <summary>
        /// Test helper that adds a quality case with the given question, reference query and status.
        /// </summary>
        public async Task<QualityCase> AddCase(string question, string fetch = Open, bool verified = true, string status = QualityCase.StatusActive, string? compare = null)
        {
            var item = new QualityCase { CreatedBy = "boss", CreatedAt = Now, Source = QualityCase.SourceMarked, Verified = verified, Status = status, Question = question, Entity = "incident", ReferenceFetch = fetch, CompareJson = compare };
            await Cases.AddAsync(item);
            return item;
        }

        /// <summary>
        /// Test helper that starts a run with the given repeat count and waits for it to finish.
        /// </summary>
        public async Task<QualityRun> RunToEnd(int repeat = 1)
        {
            var run = await Service.StartAsync(@"LAB\boss", Caller.ForEvaluation(), repeat);
            for (var i = 0; i < 400; i++)
            {
                var read = await Service.GetAsync(run.Id);
                if (read is not null && !QualityRunStatus.IsActive(read.Status)) { return read; }
                await Task.Delay(10);
            }
            throw new TimeoutException("the run did not finish");
        }

        /// <summary>
        /// Test helper that returns the stored result for a case within a run.
        /// </summary>
        public async Task<QualityResult> ResultOf(QualityRun run, QualityCase item) => (await Runs.ListResultsAsync(run.Id)).Single(r => r.CaseId == item.Id);
    }

    /// <summary>
    /// Verifies that every active case gets a verdict and retired cases are left out.
    /// </summary>
    [Fact]
    public async Task EveryActiveCase_GetsAVerdict_AndRetiredOnesAreLeftOut()
    {
        var h = new Harness();
        var good = await h.AddCase("good");
        var bad = await h.AddCase("fail");
        var denied = await h.AddCase("denied");
        var closed = await h.AddCase("closed", Closed);
        await h.AddCase("retired", status: QualityCase.StatusRetired);

        var run = await h.RunToEnd();

        Assert.Equal(QualityRunStatus.Done, run.Status);
        Assert.Equal(4, run.Total);
        Assert.Equal(4, run.Done);
        Assert.Equal(QualityVerdict.Pass, (await h.ResultOf(run, good)).Verdict);
        Assert.Equal(QualityVerdict.Fail, (await h.ResultOf(run, bad)).Verdict);
        Assert.Equal(QualityVerdict.Unreadable, (await h.ResultOf(run, denied)).Verdict);
        Assert.Equal(QualityVerdict.Skipped, (await h.ResultOf(run, closed)).Verdict);
        Assert.Contains(h.Audit, a => a.StartsWith(@"LAB\boss|quality.run|"));
        Assert.DoesNotContain(h.Audit, a => a.Contains("good"));
    }

    /// <summary>
    /// Verifies that the stored comparison detail never contains cell values.
    /// </summary>
    [Fact]
    public async Task TheStoredDetail_NeverContainsCellValues()
    {
        var h = new Harness();
        h.Runner.Script = (c, n) => new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, new[] { "SECRETVALUE", "A", "B", "C" }.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 10);
        var unordered = await h.AddCase("unordered", compare: "{\"order\":\"unordered\"}");
        var ordered = await h.AddCase("ordered", compare: "{\"order\":\"ordered\"}");

        var run = await h.RunToEnd();

        foreach (var item in new[] { unordered, ordered })
        {
            var result = await h.ResultOf(run, item);
            Assert.Equal(QualityVerdict.Fail, result.Verdict);
            Assert.DoesNotContain("SECRETVALUE", result.Detail);
        }
    }

    /// <summary>
    /// Verifies that a runner failure is recorded as an error and the question never reaches the user.
    /// </summary>
    [Fact]
    public async Task ARunnerFailure_IsAnError_AndTheQuestionNeverReachesTheUser()
    {
        var h = new Harness();
        var asked = await h.AddCase("question");

        var run = await h.RunToEnd();

        var result = await h.ResultOf(run, asked);
        Assert.Equal(QualityVerdict.Error, result.Verdict);
        Assert.Equal("asked a question", result.Detail);
    }

    /// <summary>
    /// Verifies that repeats turn mixed outcomes into flaky with pass counts.
    /// </summary>
    [Fact]
    public async Task Repeats_TurnMixedOutcomesIntoFlaky_WithPassCounts()
    {
        var h = new Harness();
        var flaky = await h.AddCase("flaky");

        var run = await h.RunToEnd(repeat: 2);

        var result = await h.ResultOf(run, flaky);
        Assert.Equal(QualityVerdict.Flaky, result.Verdict);
        Assert.Equal((1, 2), (result.Passes, result.Runs));
        Assert.Equal(2, h.Runner.Calls);
    }

    /// <summary>
    /// Verifies that a reference that cannot run is reported without blaming the model.
    /// </summary>
    [Fact]
    public async Task AReferenceThatCannotRun_IsReportedWithoutBlamingTheModel()
    {
        var h = new Harness();
        var item = await h.AddCase("good");

        h.Executor.Denied = true;
        var denied = await h.RunToEnd();
        h.Executor.Denied = false;
        h.Executor.Broken = true;
        var broken = await h.RunToEnd();

        Assert.Equal(QualityVerdict.Unreadable, (await h.ResultOf(denied, item)).Verdict);
        Assert.Equal(QualityVerdict.Error, (await h.ResultOf(broken, item)).Verdict);
        Assert.Equal(0, h.Runner.Calls);
    }

    /// <summary>
    /// Verifies that the case's own compare rule is used.
    /// </summary>
    [Fact]
    public async Task TheCompareRuleOfTheCase_IsUsed()
    {
        var h = new Harness();
        h.Runner.Script = (c, n) => new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, new[] { "C", "B", "A" }.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 10);
        var ordered = await h.AddCase("ordered", compare: "{\"order\":\"ordered\"}");
        var unordered = await h.AddCase("unordered", compare: "{\"order\":\"unordered\"}");

        var run = await h.RunToEnd();

        Assert.Equal(QualityVerdict.Fail, (await h.ResultOf(run, ordered)).Verdict);
        Assert.Equal(QualityVerdict.Pass, (await h.ResultOf(run, unordered)).Verdict);
    }

    /// <summary>
    /// Verifies that changed reference data is flagged and does not turn a pass into a fail.
    /// </summary>
    [Fact]
    public async Task ChangedReferenceData_IsFlagged_AndDoesNotTurnAPassIntoAFail()
    {
        var h = new Harness();
        var item = await h.AddCase("good");
        var first = await h.RunToEnd();
        h.Now = h.Now.AddHours(1);
        h.Executor.Rows = new[] { "A", "B", "C", "D" };
        h.Runner.Script = (c, n) => new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, new[] { "A", "B", "C", "D" }.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 10);

        var second = await h.RunToEnd();

        Assert.False((await h.ResultOf(first, item)).DataChanged);
        var result = await h.ResultOf(second, item);
        Assert.True(result.DataChanged);
        Assert.Equal(QualityVerdict.Pass, result.Verdict);
        Assert.Equal(4, result.ReferenceRows);
    }

    /// <summary>
    /// Verifies that the report shows accuracy on verified cases and regressions and leaves skipped cases out.
    /// </summary>
    [Fact]
    public async Task TheReport_ShowsAccuracyOnVerifiedCases_Regressions_AndLeavesSkippedOutOfIt()
    {
        var h = new Harness();
        var steady = await h.AddCase("good");
        var slipping = await h.AddCase("will-fail");
        await h.AddCase("closed", Closed);
        await h.AddCase("draft-case", verified: false);
        var first = await h.RunToEnd();
        h.Now = h.Now.AddHours(1);
        h.Runner.Script = (c, n) => c.Question == "will-fail"
            ? new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, new[] { "Z" }.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 300)
            : new QualityCandidateRun("answer", new ResultTable(new[] { "Case Title" }, new[] { "A", "B", "C" }.Select(r => (IReadOnlyList<string>)new[] { r }).ToList()), "", 100);

        var second = await h.RunToEnd();
        var report = (await h.Service.ReportAsync(second.Id))!.Value.Report;
        var firstReport = (await h.Service.ReportAsync(first.Id))!.Value.Report;

        Assert.Equal(4, report.Total);
        Assert.Equal(3, report.Verified);
        Assert.Equal(1, report.Unverified);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(50.0, report.AccuracyPercent);
        var regression = Assert.Single(report.Regressions);
        Assert.Equal("will-fail", regression.Question);
        Assert.True(report.HasPrevious);
        Assert.False(firstReport.HasPrevious);
        Assert.Equal(100.0, firstReport.AccuracyPercent);
        Assert.Contains(report.Problems, p => p.Question == "will-fail");
        Assert.True(report.Latency.MaxMs >= 300);
        Assert.Equal(steady.Id, (await h.Runs.ListResultsAsync(second.Id)).Single(r => r.Question == "good").CaseId);
    }

    /// <summary>
    /// Verifies that only one run can be active at a time and an empty set is refused.
    /// </summary>
    [Fact]
    public async Task OnlyOneRunAtATime_AndAnEmptySetIsRefused()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<QualityRunException>(() => h.Service.StartAsync("a", Caller.ForEvaluation(), 1));
        await h.AddCase("good");
        var gate = new TaskCompletionSource();
        h.Runner.Delay = _ => gate.Task;

        var run = await h.Service.StartAsync("a", Caller.ForEvaluation(), 1);
        var busy = await Assert.ThrowsAsync<QualityRunException>(() => h.Service.StartAsync("b", Caller.ForEvaluation(), 1));
        var tooMany = await Assert.ThrowsAsync<QualityRunException>(() => h.Service.StartAsync("b", Caller.ForEvaluation(), 9));
        gate.SetResult();
        for (var i = 0; i < 200 && QualityRunStatus.IsActive((await h.Service.GetAsync(run.Id))!.Status); i++) { await Task.Delay(10); }

        Assert.Equal("busy", busy.Code);
        Assert.Equal("invalid", tooMany.Code);
        await h.Service.StartAsync("a", Caller.ForEvaluation(), 1);
    }

    /// <summary>
    /// Verifies that cancel stops the run between cases and keeps the work already done.
    /// </summary>
    [Fact]
    public async Task Cancel_StopsTheRunBetweenCases_AndKeepsWhatWasDone()
    {
        var h = new Harness();
        for (var i = 0; i < 5; i++) { await h.AddCase("q" + i); }
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        h.Runner.Delay = async _ => { started.TrySetResult(); await gate.Task; };

        var run = await h.Service.StartAsync("a", Caller.ForEvaluation(), 1);
        await started.Task;
        Assert.True(h.Service.Cancel(run.Id));
        gate.SetResult();
        QualityRun read;
        do { await Task.Delay(10); read = (await h.Service.GetAsync(run.Id))!; } while (QualityRunStatus.IsActive(read.Status));

        Assert.Equal(QualityRunStatus.Cancelled, read.Status);
        Assert.True(read.Done < 5);
        Assert.NotNull(read.FinishedAt);
        Assert.False(h.Service.Cancel(run.Id));
    }

    /// <summary>
    /// Verifies that the time limit stops the run as failed.
    /// </summary>
    [Fact]
    public async Task TheTimeLimit_StopsTheRun_AsFailed()
    {
        var h = new Harness { Timeout = TimeSpan.FromMilliseconds(50) };
        await h.AddCase("q");
        h.Runner.Delay = ct => Task.Delay(TimeSpan.FromSeconds(30), ct);
        h.Runner.Script = null;

        var run = await h.RunToEnd();

        Assert.Equal(QualityRunStatus.Failed, run.Status);
        Assert.Contains("too long", run.Error);
    }

    /// <summary>
    /// Verifies that recovery fails runs left active by a restart.
    /// </summary>
    [Fact]
    public async Task Recover_FailsRunsLeftActiveByARestart()
    {
        var h = new Harness();
        await h.Runs.AddRunAsync(new QualityRun { StartedBy = "a", StartedAt = h.Now, Status = QualityRunStatus.Running });

        Assert.Equal(1, await h.Service.RecoverAsync());

        Assert.Equal(QualityRunStatus.Failed, (await h.Runs.ListRunsAsync(5)).Single().Status);
    }
}
