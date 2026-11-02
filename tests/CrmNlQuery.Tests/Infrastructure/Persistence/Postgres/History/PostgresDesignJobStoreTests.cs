using Npgsql;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

/// <summary>
/// Live PostgreSQL tests for the design job store, each using its own random owner and cleaning up.
/// </summary>
[Trait("Category", "Live")]
public class PostgresDesignJobStoreTests
{
    private static async Task CleanAsync(string owner)
    {
        await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
        await using var command = db.CreateCommand("delete from design_jobs where owner = @o");
        command.Parameters.AddWithValue("o", owner);
        await command.ExecuteNonQueryAsync();
    }

    private static DesignJob Job(string owner, string status = DesignJobStatus.Queued, DateTimeOffset? created = null) => new DesignJob
    {
        Id = Guid.NewGuid().ToString("N"), Owner = owner, Status = status, Step = "s", Title = "Başlık", Prefix = "new", Request = "Talep metni ü ğ",
        CreatedAt = created ?? DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Verifies that a job round trips updates stay with the owner and the list does not load the result.
    /// </summary>
    [Fact]
    public async Task AJob_RoundTrips_UpdatesStayWithTheOwner_AndTheListDoesNotLoadTheResult()
    {
        var store = await PostgresDesignJobStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var owner = "EXPORTTEST\\" + Guid.NewGuid().ToString("N");
        try
        {
            var job = Job(owner);
            await store.AddAsync(job);
            job.Status = DesignJobStatus.Ready;
            job.Percent = 100;
            job.Result = "{\"x\":1}";
            job.ProblemCount = 2;
            job.FinishedAt = DateTimeOffset.UtcNow;
            await store.UpdateAsync(job);

            var read = (await store.GetAsync(owner.ToUpperInvariant(), job.Id))!;
            Assert.Equal("Talep metni ü ğ", read.Request);
            Assert.Equal("{\"x\":1}", read.Result);
            Assert.Equal(2, read.ProblemCount);
            Assert.Equal(DesignJobStatus.Ready, read.Status);
            Assert.Null(await store.GetAsync("someone-else", job.Id));
            Assert.Null((await store.ListAsync(owner, 10)).Single().Result);
            Assert.False(await store.DeleteAsync("someone-else", job.Id));
            Assert.True(await store.DeleteAsync(owner, job.Id));
        }
        finally
        {
            await CleanAsync(owner);
        }
    }

    /// <summary>
    /// Verifies that active jobs fail on restart and purge skips active ones.
    /// </summary>
    [Fact]
    public async Task ActiveJobsFailOnRestart_AndPurgeSkipsActiveOnes()
    {
        var store = await PostgresDesignJobStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var owner = "EXPORTTEST\\" + Guid.NewGuid().ToString("N");
        try
        {
            var running = Job(owner, DesignJobStatus.Running, DateTimeOffset.UtcNow.AddDays(-400));
            var done = Job(owner, DesignJobStatus.Ready, DateTimeOffset.UtcNow.AddDays(-400));
            await store.AddAsync(running);
            await store.AddAsync(done);

            Assert.True(await store.FailActiveAsync("restart") >= 1);
            Assert.Equal(DesignJobStatus.Failed, (await store.GetAsync(owner, running.Id))!.Status);

            Assert.True(await store.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-90)) >= 2);
        }
        finally
        {
            await CleanAsync(owner);
        }
    }
}
