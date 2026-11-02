using Npgsql;
using CrmNlQuery.Domain.Feedback.Constants;
using CrmNlQuery.Domain.Feedback.Entities;
using CrmNlQuery.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Application.Feedback.Models;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

/// <summary>
/// Live tests of the PostgreSQL feedback store against a real database, using a random owner per test and cleaning up.
/// </summary>
[Trait("Category", "Live")]
public class PostgresFeedbackStoreTests
{
    private static readonly DateTimeOffset Ancient = new DateTimeOffset(1901, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task CleanAsync(Guid owner)
    {
        await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
        await using var command = db.CreateCommand("delete from feedback where owner = @o or updated_at < '1950-01-01'; delete from history where owner = @o");
        command.Parameters.AddWithValue("o", owner);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> OwnerOfAsync(string resultId)
    {
        await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
        await using var command = db.CreateCommand("select owner from feedback where result_id = @id");
        command.Parameters.AddWithValue("id", Guid.ParseExact(resultId, "N"));
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return value is DBNull ? null : (Guid)value;
    }

    private static FeedbackVote Vote(string id, Guid owner, string rating, string? reason, bool shared, int? steps = 1)
        => new FeedbackVote(id, owner, rating, reason, shared, new[] { "account", "contact" }, steps, 1500, Ancient, Ancient);

    /// <summary>
    /// Verifies that vote candidate and summary work end to end and a cascade delete takes the candidate.
    /// </summary>
    [Fact]
    public async Task VoteCandidateAndSummary_WorkEndToEnd_AndACascadeDeleteTakesTheCandidate()
    {
        var owner = Guid.NewGuid();
        var history = await PostgresHistoryStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var store = await PostgresFeedbackStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        try
        {
            var id = await history.AddAsync(owner, "Show accounts in London", "account", "<fetch />", steps: 2, durationMs: 900);
            Assert.Equal(2, (await history.GetAsync(owner, id))!.Steps);

            await store.UpsertAsync(Vote(id, owner, "down", FeedbackReasons.WrongRecords, true, 2),
                new FeedbackCandidate(id, "Show accounts in London", "Account · City eq London", FeedbackReasons.WrongRecords, Ancient));

            var states = await store.GetStatesAsync(owner, new[] { id });
            Assert.Equal(new FeedbackState("down", FeedbackReasons.WrongRecords, true), states[id]);
            Assert.Empty(await store.GetStatesAsync(Guid.NewGuid(), new[] { id }));

            var summary = await store.SummaryAsync(Ancient.AddDays(-1), Ancient.AddDays(1));
            Assert.Equal(1, summary.Down);
            Assert.Equal(1, summary.DownMultiStep);
            Assert.Contains(new FeedbackCount(FeedbackReasons.WrongRecords, 1), summary.Reasons);
            Assert.Contains(new FeedbackCount("contact", 1), summary.DownTables);
            Assert.Equal(new FeedbackDay(DateOnly.FromDateTime(Ancient.UtcDateTime), 0, 1), Assert.Single(summary.Days));

            Assert.True(await history.DeleteAsync(owner, id));
            Assert.Empty(await store.GetStatesAsync(owner, new[] { id }));
            Assert.False(await store.DeleteCandidateAsync(id));
            Assert.Null(await OwnerOfAsync(id));
            Assert.Equal(1, (await store.SummaryAsync(Ancient.AddDays(-1), Ancient.AddDays(1))).Down);
        }
        finally
        {
            await CleanAsync(owner);
        }
    }

    /// <summary>
    /// Verifies that withdrawing removing and ownership behave like the memory store.
    /// </summary>
    [Fact]
    public async Task Withdrawing_Removing_AndOwnership_BehaveLikeTheMemoryStore()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var history = await PostgresHistoryStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var store = await PostgresFeedbackStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        try
        {
            var id = await history.AddAsync(owner, "q", "account", "<fetch />");
            await store.UpsertAsync(Vote(id, owner, "down", null, true), new FeedbackCandidate(id, "q", "e", null, Ancient));

            await store.UpsertAsync(Vote(id, other, "up", null, false), null);
            Assert.Equal("down", (await store.GetStatesAsync(owner, new[] { id }))[id].Rating);

            await store.UpsertAsync(Vote(id, owner, "down", null, false), null);
            var state = (await store.GetStatesAsync(owner, new[] { id }))[id];
            Assert.False(state.Shared);

            Assert.False(await store.RemoveAsync(other, id));
            Assert.True(await store.RemoveAsync(owner, id));
            Assert.Empty(await store.GetStatesAsync(owner, new[] { id }));
        }
        finally
        {
            await CleanAsync(owner);
        }
    }

    /// <summary>
    /// Verifies that deleting the whole chat or all chats also removes the user from the vote and removes the candidate.
    /// </summary>
    [Fact]
    public async Task DeletingTheWholeChat_AndDeleteAll_AlsoRemoveTheUserFromTheVote_AndTheCandidate()
    {
        var owner = Guid.NewGuid();
        var history = await PostgresHistoryStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var store = await PostgresFeedbackStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        try
        {
            var chat = await history.CreateConversationAsync(owner, "chat");
            var first = await history.AddAsync(owner, "q1", "account", "<fetch />", null, chat);
            await store.UpsertAsync(Vote(first, owner, "down", null, true), new FeedbackCandidate(first, "q1", "e", null, Ancient));
            Assert.True(await history.DeleteConversationAsync(owner, chat));
            Assert.Null(await OwnerOfAsync(first));
            Assert.DoesNotContain(await store.ListCandidatesAsync(100000), c => c.ResultId == first);

            var chat2 = await history.CreateConversationAsync(owner, "chat 2");
            var second = await history.AddAsync(owner, "q2", "account", "<fetch />", null, chat2);
            await store.UpsertAsync(Vote(second, owner, "down", null, true), new FeedbackCandidate(second, "q2", "e", null, Ancient));
            await history.DeleteAllConversationsAsync(owner, true);
            Assert.Null(await OwnerOfAsync(second));
            Assert.DoesNotContain(await store.ListCandidatesAsync(100000), c => c.ResultId == second);
        }
        finally
        {
            await CleanAsync(owner);
        }
    }

    /// <summary>
    /// Verifies that saving a candidate for a missing result returns false instead of failing.
    /// </summary>
    [Fact]
    public async Task SavingACandidateForAMissingResult_ReturnsFalse_InsteadOfFailing()
    {
        var owner = Guid.NewGuid();
        var store = await PostgresFeedbackStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var missing = Guid.NewGuid().ToString("N");
        try
        {
            Assert.False(await store.UpsertAsync(Vote(missing, owner, "down", null, true), new FeedbackCandidate(missing, "q", "e", null, Ancient)));
        }
        finally
        {
            await CleanAsync(owner);
        }
    }

    /// <summary>
    /// Verifies that purge deletes old votes and candidates by their own cutoffs.
    /// </summary>
    [Fact]
    public async Task Purge_DeletesOldVotesAndCandidates_ByTheirOwnCutoffs()
    {
        var owner = Guid.NewGuid();
        var history = await PostgresHistoryStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var store = await PostgresFeedbackStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        try
        {
            var id = await history.AddAsync(owner, "q", "account", "<fetch />");
            await store.UpsertAsync(Vote(id, owner, "down", null, true), new FeedbackCandidate(id, "q", "e", null, Ancient));

            await store.PurgeAsync(new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
            Assert.Empty(await store.GetStatesAsync(owner, new[] { id }));
            Assert.Contains(await store.ListCandidatesAsync(100000), c => c.ResultId == id);

            await store.PurgeAsync(null, new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.DoesNotContain(await store.ListCandidatesAsync(100000), c => c.ResultId == id);

        }
        finally
        {
            await CleanAsync(owner);
        }
    }
}
