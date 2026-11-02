using Npgsql;
using CrmNlQuery.Application.Export.Abstractions;
using CrmNlQuery.Application.Export.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Tests the PostgreSQL export pager against a real database using only rows written to year 1901.
/// </summary>
[Trait("Category", "Live")]
public class PostgresExportPagerTests
{
    private static readonly DateTimeOffset Ancient = new DateTimeOffset(1901, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = new DateTimeOffset(1901, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new DateTimeOffset(1902, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that audit paging returns every row once oldest first and honours the filters.
    /// </summary>
    [Fact]
    public async Task AuditPaging_ReturnsEveryRowOnce_OldestFirst_AndHonoursTheFilters()
    {
        var store = await PostgresAccessStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
        try
        {
            for (var i = 0; i < 25; i++)
            {
                await store.AddAuditAsync(new AuditEntry(Ancient.AddSeconds(i / 5), "EXPORTTEST\\pager", i % 2 == 0 ? "user.add" : "role.create", "t" + i, ""));
            }
            IAuditPager pager = store;

            var seen = new List<string>();
            AuditCursor? cursor = null;
            while (true)
            {
                var page = await pager.PageAuditAsync(From, To, "exporttest\\PAGER", null, cursor, 7);
                seen.AddRange(page.Select(p => p.Entry.Target));
                if (page.Count < 7) { break; }
                cursor = new AuditCursor(page[^1].Entry.At, page[^1].Id);
            }
            var roles = await pager.PageAuditAsync(From, To, "EXPORTTEST\\pager", "role.", null, 100);

            Assert.Equal(Enumerable.Range(0, 25).Select(i => "t" + i), seen);
            Assert.Equal(12, roles.Count);
            Assert.Empty(await pager.PageAuditAsync(From, To, "EXPORTTEST\\pager", "user.%", null, 10));

            await store.AddAuditAsync(new AuditEntry(Ancient.AddMinutes(5), "EXPORTTEST\\pager", "access.denied", "d1", ""));
            await store.AddAuditAsync(new AuditEntry(Ancient.AddMinutes(6), "EXPORTTEST\\pager", "access_denied", "d2", ""));
            var underscore = await pager.PageAuditAsync(From, To, "EXPORTTEST\\pager", "access_denied", null, 10);
            Assert.Equal(new[] { "d2" }, underscore.Select(p => p.Entry.Target));
        }
        finally
        {
            await using var delete = db.CreateCommand("delete from admin_audit where at < '1950-01-01'");
            await delete.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Verifies that question paging returns only marked questions every row once and honours the user filter.
    /// </summary>
    [Fact]
    public async Task QuestionPaging_ReturnsOnlyMarkedQuestions_EveryRowOnce_AndHonoursTheUserFilter()
    {
        var store = await PostgresHistoryStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var ann = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
        try
        {
            for (var i = 0; i < 17; i++)
            {
                await store.AddAsync(i < 12 ? ann : bob, "soru " + i, "account", "<fetch/>", adminViewable: i % 5 != 4);
            }
            IQuestionPager pager = store;
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow.AddDays(1);

            var seen = new List<string>();
            QuestionCursor? cursor = null;
            while (true)
            {
                var page = await pager.PageQuestionsAsync(from, to, ann, cursor, 4);
                seen.AddRange(page.Select(p => p.Question));
                if (page.Count < 4) { break; }
                cursor = new QuestionCursor(page[^1].At, page[^1].Id);
            }
            var bobs = (await pager.PageQuestionsAsync(from, to, bob, null, 100)).Select(p => p.Question).ToList();

            var expectedAnn = Enumerable.Range(0, 12).Where(i => i % 5 != 4).Select(i => "soru " + i).ToList();
            Assert.Equal(expectedAnn.OrderBy(x => x), seen.OrderBy(x => x));
            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.DoesNotContain(bobs, q => expectedAnn.Contains(q));
        }
        finally
        {
            await using var delete = db.CreateCommand("delete from history where owner = @a or owner = @b");
            delete.Parameters.AddWithValue("a", ann);
            delete.Parameters.AddWithValue("b", bob);
            await delete.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Verifies that access log paging returns every row once and honours user table and source.
    /// </summary>
    [Fact]
    public async Task AccessLogPaging_ReturnsEveryRowOnce_AndHonoursUserTableAndSource()
    {
        var store = await PostgresAccessLogStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var user = Guid.NewGuid();
        try
        {
            for (var i = 0; i < 23; i++)
            {
                await store.AddAsync(new DataAccessRecord(Guid.NewGuid(), Ancient.AddSeconds(i / 4), i < 10 ? user : Guid.NewGuid(), i % 2 == 0 ? "ask" : "lookup",
                    new[] { "account", i % 3 == 0 ? "contact" : "product" }, i, "ok", Guid.NewGuid()));
            }
            IAccessLogPager pager = store;
            var query = new DataAccessQuery(From, To, null, null);

            var seen = new List<Guid>();
            AccessCursor? cursor = null;
            while (true)
            {
                var page = await pager.PageAsync(query, cursor, 6);
                seen.AddRange(page.Select(p => p.Id));
                if (page.Count < 6) { break; }
                cursor = new AccessCursor(page[^1].At, page[^1].Id);
            }

            var mine = await pager.PageAsync(query with { User = user }, null, 100);
            var contacts = await pager.PageAsync(query with { Table = "contact" }, null, 100);
            var lookups = await pager.PageAsync(query with { Source = "lookup" }, null, 100);

            Assert.Equal(23, seen.Count);
            Assert.Equal(23, seen.Distinct().Count());
            Assert.Equal(10, mine.Count);
            Assert.Equal(Enumerable.Range(0, 23).Count(i => i % 3 == 0), contacts.Count);
            Assert.Equal(Enumerable.Range(0, 23).Count(i => i % 2 == 1), lookups.Count);
            Assert.True(mine.Zip(mine.Skip(1)).All(p => p.First.At <= p.Second.At));
        }
        finally
        {
            await store.PurgeOlderThanAsync(new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }
    }
}
