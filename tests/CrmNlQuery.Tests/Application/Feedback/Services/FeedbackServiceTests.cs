using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.Feedback.Models;
using CrmNlQuery.Application.Feedback.Services;
using CrmNlQuery.Domain.Feedback.Constants;
using CrmNlQuery.Domain.Feedback.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.Feedback.Services;

/// <summary>
/// Tests thumbs up and down feedback: votes with metadata, opt-in sharing, withdrawal, ownership and auditing.
/// </summary>
public class FeedbackServiceTests
{
    private const string Fetch = "<fetch><entity name=\"account\"><attribute name=\"name\" /><filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter></entity></fetch>";

    private readonly MemoryHistoryStore _history = new MemoryHistoryStore();
    private readonly MemoryFeedbackStore _store;
    private readonly AccessService _access = new AccessService(new MemoryAccessStore(), new string[0]);
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private bool _enabled = true;
    private bool _sharing = true;
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public FeedbackServiceTests()
    {
        _store = new MemoryFeedbackStore(_history.Exists);
    }

    private FeedbackService Service() => new FeedbackService(_store, _history, new QueryExplainer(new FakeMetadataSource(TestMetadata.Account())), _access,
        () => _enabled, () => _sharing, () => _now);

    private Task<string> AddAsync(Guid owner, string question = "Show accounts in London", int? steps = 1, int? duration = 1200)
        => _history.AddAsync(owner, question, "account", Fetch, steps: steps, durationMs: duration);

    /// <summary>
    /// Verifies that a thumbs up is stored with metadata and no question text.
    /// </summary>
    [Fact]
    public async Task ThumbsUp_IsStoredWithMeta_AndNoQuestionText()
    {
        var id = await AddAsync(_alice, steps: 2, duration: 3400);

        var result = await Service().RateAsync(_alice, id, "up", null, false);

        Assert.Equal(FeedbackStatus.Ok, result.Status);
        Assert.Equal(new FeedbackState("up", null, false), result.State);
        var summary = await Service().SummaryAsync(7);
        Assert.Equal(1, summary.Up);
        Assert.Empty(await _store.ListCandidatesAsync(10));
    }

    /// <summary>
    /// Verifies that a thumbs down without sharing keeps only the reason and metadata.
    /// </summary>
    [Fact]
    public async Task ThumbsDown_WithoutSharing_KeepsOnlyReasonAndMeta()
    {
        var id = await AddAsync(_alice, steps: 3);

        await Service().RateAsync(_alice, id, "down", FeedbackReasons.WrongRecords, false);

        Assert.Empty(await _store.ListCandidatesAsync(10));
        var summary = await Service().SummaryAsync(7);
        Assert.Equal(1, summary.Down);
        Assert.Equal(1, summary.DownMultiStep);
        Assert.Equal(new FeedbackCount(FeedbackReasons.WrongRecords, 1), Assert.Single(summary.Reasons));
        Assert.Equal(new FeedbackCount("account", 1), Assert.Single(summary.DownTables));
    }

    /// <summary>
    /// Verifies that sharing is only for thumbs down and only when allowed.
    /// </summary>
    [Fact]
    public async Task SharingIsOnlyForThumbsDown_AndOnlyWhenAllowed()
    {
        var id = await AddAsync(_alice);

        Assert.Equal(FeedbackStatus.Invalid, (await Service().RateAsync(_alice, id, "up", null, true)).Status);
        _sharing = false;
        Assert.Equal(FeedbackStatus.Invalid, (await Service().RateAsync(_alice, id, "down", FeedbackReasons.Other, true)).Status);
        Assert.Empty(await _store.ListCandidatesAsync(10));
    }

    /// <summary>
    /// Verifies that a shared question becomes a candidate with the question and explanation but no user or rows.
    /// </summary>
    [Fact]
    public async Task SharedQuestion_BecomesACandidate_WithQuestionAndExplanation_AndNoUserOrRows()
    {
        var id = await AddAsync(_alice);

        var result = await Service().RateAsync(_alice, id, "down", FeedbackReasons.Misunderstood, true);

        Assert.True(result.State!.Shared);
        var candidate = Assert.Single(await _store.ListCandidatesAsync(10));
        Assert.Equal("Show accounts in London", candidate.Question);
        Assert.Contains("Account", candidate.Explanation);
        Assert.Contains("London", candidate.Explanation);
        Assert.DoesNotContain("<fetch", candidate.Explanation);
        Assert.Equal(FeedbackReasons.Misunderstood, candidate.Reason);
    }

    /// <summary>
    /// Verifies that withdrawing deletes the candidate but keeps the rating.
    /// </summary>
    [Fact]
    public async Task Withdrawing_DeletesTheCandidate_ButKeepsTheRating()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", FeedbackReasons.Other, true);

        var again = await Service().RateAsync(_alice, id, "down", FeedbackReasons.Other, false);

        Assert.False(again.State!.Shared);
        Assert.Empty(await _store.ListCandidatesAsync(10));
        Assert.Equal(1, (await Service().SummaryAsync(7)).Down);
    }

    /// <summary>
    /// Verifies that changing to thumbs up removes the shared candidate.
    /// </summary>
    [Fact]
    public async Task ChangingToThumbsUp_RemovesTheSharedCandidate()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);

        await Service().RateAsync(_alice, id, "up", null, false);

        Assert.Empty(await _store.ListCandidatesAsync(10));
    }

    /// <summary>
    /// Verifies that a removed vote leaves no candidate and other users cannot remove it.
    /// </summary>
    [Fact]
    public async Task ARemovedVote_LeavesNoCandidate_AndOtherUsersCannotRemoveIt()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);

        Assert.Equal(FeedbackStatus.NotFound, (await Service().RemoveAsync(_bob, id)).Status);
        Assert.Equal(FeedbackStatus.Ok, (await Service().RemoveAsync(_alice, id)).Status);

        Assert.Empty(await _store.ListCandidatesAsync(10));
        Assert.Equal(0, (await Service().SummaryAsync(7)).Down);
    }

    /// <summary>
    /// Verifies that someone else's result, an unknown id and a disabled feature are all not found.
    /// </summary>
    [Fact]
    public async Task SomeoneElsesResult_UnknownId_AndDisabledFeature_AreAllNotFound()
    {
        var id = await AddAsync(_alice);

        Assert.Equal(FeedbackStatus.NotFound, (await Service().RateAsync(_bob, id, "up", null, false)).Status);
        Assert.Equal(FeedbackStatus.NotFound, (await Service().RateAsync(_alice, "nope", "up", null, false)).Status);
        _enabled = false;
        Assert.Equal(FeedbackStatus.NotFound, (await Service().RateAsync(_alice, id, "up", null, false)).Status);
        Assert.False(Service().Enabled);
        Assert.False(Service().AllowSharing);
    }

    /// <summary>
    /// Verifies that an invalid rating or reason is rejected, including a reason on a thumbs up.
    /// </summary>
    [Fact]
    public async Task InvalidRatingOrReason_IsRejected_AndAReasonOnThumbsUpToo()
    {
        var id = await AddAsync(_alice);

        Assert.Equal(FeedbackStatus.Invalid, (await Service().RateAsync(_alice, id, "meh", null, false)).Status);
        Assert.Equal(FeedbackStatus.Invalid, (await Service().RateAsync(_alice, id, "down", "free text", false)).Status);
        Assert.Equal(FeedbackStatus.Invalid, (await Service().RateAsync(_alice, id, "up", FeedbackReasons.Other, false)).Status);
    }

    /// <summary>
    /// Verifies that deleting the history entry removes the shared candidate and detaches the user from the vote.
    /// </summary>
    [Fact]
    public async Task DeletingTheHistoryEntry_TakesTheSharedCandidateAway_AndRemovesTheUserFromTheVote()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);

        Assert.Equal(_alice, _store.OwnerOf(id));

        await _history.DeleteAsync(_alice, id);

        Assert.Empty(await _store.ListCandidatesAsync(10));
        Assert.Null(_store.OwnerOf(id));
        Assert.Empty(await Service().StatesAsync(_alice, new[] { id }));
        Assert.Equal(1, (await Service().SummaryAsync(7)).Down);
    }

    /// <summary>
    /// Verifies that every candidate view is audited without text and deletion is audited too.
    /// </summary>
    [Fact]
    public async Task EveryCandidateView_IsAudited_WithoutText_AndDeleteIsAuditedToo()
    {
        var id = await AddAsync(_alice, "secret question");
        await Service().RateAsync(_alice, id, "down", null, true);

        await Service().CandidatesAsync(@"LAB\Boss", 50);
        await Service().CandidatesAsync(@"LAB\Boss", 50);
        Assert.True(await Service().DeleteCandidateAsync(@"LAB\Boss", id));

        var audit = (await _access.ListAuditAsync(10)).ToList();
        Assert.Equal(2, audit.Count(a => a.Action == "feedback.view"));
        Assert.Contains(audit, a => a.Action == "feedback.view" && a.Detail == "1 candidates");
        Assert.Single(audit, a => a.Action == "feedback.delete");
        Assert.All(audit, a => Assert.DoesNotContain("secret", a.Detail + a.Target));
        Assert.False((await Service().StatesAsync(_alice, new[] { id }))[id].Shared);
    }

    /// <summary>
    /// Verifies that purge removes old candidates and votes by their own cutoffs.
    /// </summary>
    [Fact]
    public async Task Purge_RemovesOldCandidatesAndVotes_ByTheirOwnCutoffs()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);
        _now = _now.AddDays(200);

        Assert.Equal(1, await _store.PurgeAsync(null, _now.AddDays(-180)));
        Assert.Empty(await _store.ListCandidatesAsync(10));
        Assert.Equal(1, await _store.PurgeAsync(_now.AddDays(-90), null));
    }

    /// <summary>
    /// Verifies that a summary entry cannot be rated.
    /// </summary>
    [Fact]
    public async Task ASummaryEntry_CannotBeRated()
    {
        var id = await _history.AddAsync(_alice, "Summarize X", "account", Fetch, kind: "summary");

        Assert.Equal(FeedbackStatus.NotFound, (await Service().RateAsync(_alice, id, "up", null, false)).Status);
    }

    /// <summary>
    /// Verifies that purging votes does not touch candidates, which follow their own retention.
    /// </summary>
    [Fact]
    public async Task VotePurge_DoesNotTouchCandidates_TheyFollowTheirOwnRetention()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);
        _now = _now.AddDays(100);

        await _store.PurgeAsync(_now.AddDays(-90), null);

        Assert.Single(await _store.ListCandidatesAsync(10));
    }

    /// <summary>
    /// Verifies that a user can always withdraw a vote even when the feature was switched off.
    /// </summary>
    [Fact]
    public async Task AUserCanAlwaysWithdraw_EvenWhenTheFeatureWasSwitchedOff()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);
        _enabled = false;

        var result = await Service().RemoveAsync(_alice, id);

        Assert.Equal(FeedbackStatus.Ok, result.Status);
        Assert.Empty(await _store.ListCandidatesAsync(10));
    }

    /// <summary>
    /// Verifies that re sharing keeps the first shared date.
    /// </summary>
    [Fact]
    public async Task ReSharing_KeepsTheFirstSharedDate()
    {
        var id = await AddAsync(_alice);
        await Service().RateAsync(_alice, id, "down", null, true);
        var first = (await _store.ListCandidatesAsync(10)).Single().CreatedAt;
        _now = _now.AddDays(3);

        await Service().RateAsync(_alice, id, "down", FeedbackReasons.Other, true);

        Assert.Equal(first, (await _store.ListCandidatesAsync(10)).Single().CreatedAt);
    }

    /// <summary>
    /// Verifies that the tables-of function lists the root and linked tables without values.
    /// </summary>
    [Fact]
    public void TablesOf_ListsRootAndLinkedTables_WithoutValues()
    {
        var tables = FeedbackService.TablesOf("<fetch><entity name=\"contact\"><link-entity name=\"account\" from=\"accountid\" to=\"parentcustomerid\"><link-entity name=\"account\" from=\"a\" to=\"b\" /></link-entity></entity></fetch>");

        Assert.Equal(new[] { "contact", "account" }, tables);
        Assert.Empty(FeedbackService.TablesOf("not xml"));
    }
}
