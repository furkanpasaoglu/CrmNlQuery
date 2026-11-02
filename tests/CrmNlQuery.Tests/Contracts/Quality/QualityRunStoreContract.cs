using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Tests.Contracts.Quality;

/// <summary>
/// Shared contract tests the in-memory and PostgreSQL quality run stores must pass identically, using only year 2001 data.
/// </summary>
public abstract class QualityRunStoreContract
{
    protected abstract IQualityRunStore CreateStore();

    private static QualityRun Run(DateTimeOffset at, string status = QualityRunStatus.Done) => new QualityRun
    {
        StartedBy = @"LAB\boss", StartedAt = at, FinishedAt = status == QualityRunStatus.Done ? at.AddMinutes(1) : null, Status = status, Repeat = 2, Total = 3, Done = 3, SummaryJson = "{\"x\":1}",
    };

    private static QualityResult Result(string runId, string caseId, string verdict = QualityVerdict.Pass) => new QualityResult
    {
        RunId = runId, CaseId = caseId, Question = "q " + caseId, Entity = "incident", Verified = true, Verdict = verdict, Passes = 2, Runs = 2, Detail = "ok", DurationMs = 1234,
        ReferenceRows = 7, Fingerprint = "7:abc", DataChanged = true,
    };

    private static async Task Cleanup(IQualityRunStore store) => await store.PurgeOlderThanAsync(new DateTimeOffset(2002, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Verifies that a run is read back with every field and updates.
    /// </summary>
    [Fact]
    public async Task ARun_IsReadBackWithEveryField_AndUpdates()
    {
        var store = CreateStore();
        var run = Run(new DateTimeOffset(2001, 5, 1, 10, 0, 0, TimeSpan.Zero), QualityRunStatus.Running);
        try
        {
            await store.AddRunAsync(run);
            await store.UpdateRunAsync(run with { Status = QualityRunStatus.Done, Done = 3, FinishedAt = run.StartedAt.AddMinutes(5), SummaryJson = "{\"y\":2}" });

            var read = await store.GetRunAsync(run.Id);

            Assert.NotNull(read);
            Assert.Equal(QualityRunStatus.Done, read!.Status);
            Assert.Equal(@"LAB\boss", read.StartedBy);
            Assert.Equal(2, read.Repeat);
            Assert.Equal(3, read.Total);
            Assert.Equal(3, read.Done);
            Assert.Equal("{\"y\":2}", read.SummaryJson);
            Assert.NotNull(read.FinishedAt);
        }
        finally { await Cleanup(store); }
    }

    /// <summary>
    /// Verifies that results are stored per run with every field.
    /// </summary>
    [Fact]
    public async Task Results_AreStoredPerRun_WithEveryField()
    {
        var store = CreateStore();
        var run = Run(new DateTimeOffset(2001, 5, 2, 10, 0, 0, TimeSpan.Zero));
        var caseId = Guid.NewGuid().ToString("N");
        try
        {
            await store.AddRunAsync(run);
            await store.AddResultAsync(Result(run.Id, caseId));

            var results = await store.ListResultsAsync(run.Id);

            var r = Assert.Single(results);
            Assert.Equal(caseId, r.CaseId);
            Assert.Equal("q " + caseId, r.Question);
            Assert.Equal(QualityVerdict.Pass, r.Verdict);
            Assert.Equal((2, 2, 1234, 7), (r.Passes, r.Runs, r.DurationMs, r.ReferenceRows));
            Assert.Equal("7:abc", r.Fingerprint);
            Assert.True(r.DataChanged);
            Assert.True(r.Verified);
            Assert.Empty(await store.ListResultsAsync(Guid.NewGuid().ToString("N")));
        }
        finally { await Cleanup(store); }
    }

    /// <summary>
    /// Verifies that the run list is newest first and the previous done run skips unfinished ones.
    /// </summary>
    [Fact]
    public async Task TheRunList_IsNewestFirst_AndThePreviousDoneRunSkipsUnfinishedOnes()
    {
        var store = CreateStore();
        var old = Run(new DateTimeOffset(2001, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var failed = Run(new DateTimeOffset(2001, 6, 2, 0, 0, 0, TimeSpan.Zero), QualityRunStatus.Failed);
        var current = Run(new DateTimeOffset(2001, 6, 3, 0, 0, 0, TimeSpan.Zero), QualityRunStatus.Running);
        try
        {
            foreach (var r in new[] { old, failed, current }) { await store.AddRunAsync(r); }

            var list = (await store.ListRunsAsync(50)).Where(r => r.StartedAt.Year == 2001).Select(r => r.Id).ToList();
            var previous = await store.PreviousDoneRunAsync(current);

            Assert.Equal(new[] { current.Id, failed.Id, old.Id }, list);
            Assert.Equal(old.Id, previous!.Id);
            Assert.Null(await store.PreviousDoneRunAsync(old));
        }
        finally { await Cleanup(store); }
    }

    /// <summary>
    /// Verifies that fail active marks only unfinished runs.
    /// </summary>
    [Fact]
    public async Task FailActive_MarksOnlyUnfinishedRuns()
    {
        var store = CreateStore();
        var active = Run(new DateTimeOffset(2001, 7, 1, 0, 0, 0, TimeSpan.Zero), QualityRunStatus.Running);
        var done = Run(new DateTimeOffset(2001, 7, 2, 0, 0, 0, TimeSpan.Zero));
        try
        {
            await store.AddRunAsync(active);
            await store.AddRunAsync(done);

            var n = await store.FailActiveAsync("restart");

            Assert.True(n >= 1);
            var read = await store.GetRunAsync(active.Id);
            Assert.Equal(QualityRunStatus.Failed, read!.Status);
            Assert.Equal("restart", read.Error);
            Assert.Equal(QualityRunStatus.Done, (await store.GetRunAsync(done.Id))!.Status);
        }
        finally { await Cleanup(store); }
    }

    /// <summary>
    /// Verifies that purge removes old finished runs with their results but keeps recent ones.
    /// </summary>
    [Fact]
    public async Task Purge_RemovesOldFinishedRunsWithTheirResults_ButKeepsRecentOnes()
    {
        var store = CreateStore();
        var old = Run(new DateTimeOffset(2001, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var recent = Run(new DateTimeOffset(2001, 12, 30, 0, 0, 0, TimeSpan.Zero));
        try
        {
            await store.AddRunAsync(old);
            await store.AddRunAsync(recent);
            await store.AddResultAsync(Result(old.Id, "a"));
            await store.AddResultAsync(Result(recent.Id, "b"));

            await store.PurgeOlderThanAsync(new DateTimeOffset(2001, 11, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.Null(await store.GetRunAsync(old.Id));
            Assert.Empty(await store.ListResultsAsync(old.Id));
            Assert.NotNull(await store.GetRunAsync(recent.Id));
            Assert.Single(await store.ListResultsAsync(recent.Id));
        }
        finally { await Cleanup(store); }
    }
}
