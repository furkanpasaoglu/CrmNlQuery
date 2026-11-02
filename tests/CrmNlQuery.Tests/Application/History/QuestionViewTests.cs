using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Models;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.History;

/// <summary>
/// Tests viewing question text: off by default, only questions asked while on, no separate copy.
/// </summary>
public class QuestionViewTests
{
    private sealed class FakeResolver : ICallerResolver
    {
        public Dictionary<string, Guid> Known = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        public bool Fail;

        /// <summary>
        /// Returns a canned resolution for the test.
        /// </summary>
        public Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default)
        {
            if (Fail) { throw new CrmException("CRM unreachable"); }
            return Task.FromResult<Guid?>(Known.TryGetValue(windowsAccount, out var id) ? id : null);
        }
    }

    private sealed class NoExecutor : IQueryExecutor
    {
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, CrmNlQuery.Domain.Access.Entities.Caller caller, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private readonly MemoryHistoryStore _history = new MemoryHistoryStore();
    private readonly FakeResolver _resolver = new FakeResolver();
    private readonly AccessService _access = new AccessService(new MemoryAccessStore(), new string[0]);
    private bool _enabled;
    private readonly Guid _alice = Guid.NewGuid();

    private QuestionViewService Service() => new QuestionViewService(_history, _resolver, _access, () => _enabled);

    private ResultService Results() => new ResultService(_history, null!, new NoExecutor(), () => _enabled);

    private static AnswerOutcome Answer() => new AnswerOutcome("account", "", "<fetch><entity name=\"account\" /></fetch>", new ResultTable(new[] { "name" }, new List<IReadOnlyList<string>>()), false);

    private async Task AskAsync(string question) => await Results().PublishAsync(Answer(), _alice, question);

    /// <summary>
    /// Verifies that when the setting is off questions are not marked viewable and the service refuses.
    /// </summary>
    [Fact]
    public async Task WhenTheSettingIsOff_QuestionsAreNotMarkedViewable_AndTheServiceRefuses()
    {
        _enabled = false;
        await AskAsync("asked while off");

        var result = await Service().ListByIdAsync("lab\\boss", _alice, 50);

        Assert.Equal(QuestionViewStatus.NotEnabled, result.Status);
        Assert.Empty(await _history.ListViewableQuestionsAsync(_alice, 50));
        Assert.Empty(await _access.ListAuditAsync(10));
    }

    /// <summary>
    /// Verifies that only questions asked while the setting was on are ever viewable not retroactive.
    /// </summary>
    [Fact]
    public async Task OnlyQuestionsAskedWhileTheSettingWasOn_AreEverViewable_NotRetroactive()
    {
        _enabled = false;
        await AskAsync("before");
        _enabled = true;
        await AskAsync("during");
        _enabled = false;
        await AskAsync("after");
        _enabled = true;

        var result = await Service().ListByIdAsync("lab\\boss", _alice, 50);

        Assert.Equal(QuestionViewStatus.Ok, result.Status);
        Assert.Equal(new[] { "during" }, result.Questions.Select(q => q.Question).ToArray());
    }

    /// <summary>
    /// Verifies that every view is audited even with no questions and the audit never holds the text.
    /// </summary>
    [Fact]
    public async Task EveryView_IsAudited_EvenWithNoQuestions_AndTheAuditNeverHoldsTheText()
    {
        _enabled = true;
        await AskAsync("a very sensitive question text");

        await Service().ListByIdAsync(@"LAB\Boss", _alice, 50);
        await Service().ListByIdAsync(@"LAB\Boss", _alice, 50);
        await Service().ListByIdAsync(@"LAB\Boss", Guid.NewGuid(), 50);

        var audit = (await _access.ListAuditAsync(10)).Where(a => a.Action == "questions.view").ToList();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.Equal(@"lab\boss", a.Actor));
        Assert.Contains(audit, a => a.Detail == "1 questions" && a.Target == _alice.ToString());
        Assert.Contains(audit, a => a.Detail == "0 questions");
        Assert.DoesNotContain(audit, a => (a.Target + a.Detail).Contains("sensitive"));
    }

    /// <summary>
    /// Verifies that by account the user is resolved through the CRM and an unknown account is not found.
    /// </summary>
    [Fact]
    public async Task ByAccount_TheUserIsResolvedThroughTheCrm_AndAnUnknownAccountIsNotFound()
    {
        _enabled = true;
        _resolver.Known[@"LAB\alice"] = _alice;
        await AskAsync("hello");

        var found = await Service().ListByAccountAsync(@"LAB\Boss", @"LAB\alice", 50);
        var missing = await Service().ListByAccountAsync(@"LAB\Boss", @"LAB\nobody", 50);

        Assert.Equal(QuestionViewStatus.Ok, found.Status);
        Assert.Equal("hello", Assert.Single(found.Questions).Question);
        Assert.Equal(QuestionViewStatus.NotFound, missing.Status);
        Assert.Single(await _access.ListAuditAsync(10), a => a.Action == "questions.view");
    }

    /// <summary>
    /// Verifies that when the CRM is unreachable the exception flows and nothing is audited.
    /// </summary>
    [Fact]
    public async Task WhenTheCrmIsUnreachable_TheExceptionFlows_AndNothingIsAudited()
    {
        _enabled = true;
        _resolver.Fail = true;

        await Assert.ThrowsAsync<CrmException>(() => Service().ListByAccountAsync(@"LAB\Boss", @"LAB\alice", 50));

        Assert.Empty(await _access.ListAuditAsync(10));
    }

    /// <summary>
    /// Verifies that a deleted question is gone for the admin too no separate copy is kept.
    /// </summary>
    [Fact]
    public async Task ADeletedQuestion_IsGoneForTheAdminToo_NoSeparateCopyIsKept()
    {
        _enabled = true;
        var published = await Results().PublishAsync(Answer(), _alice, "to be deleted");
        Assert.Single((await Service().ListByIdAsync("lab\\boss", _alice, 50)).Questions);

        await _history.DeleteAsync(_alice, published.ResultId!);

        Assert.Empty((await Service().ListByIdAsync("lab\\boss", _alice, 50)).Questions);
    }

    /// <summary>
    /// Verifies that take is clamped between one and two hundred.
    /// </summary>
    [Fact]
    public async Task TakeIsClamped_BetweenOneAndTwoHundred()
    {
        _enabled = true;
        for (var i = 0; i < 3; i++) { await AskAsync("q" + i); }

        Assert.Equal(3, (await Service().ListByIdAsync("lab\\boss", _alice, 1000)).Questions.Count);
        Assert.Single((await Service().ListByIdAsync("lab\\boss", _alice, -5)).Questions);
    }
}
