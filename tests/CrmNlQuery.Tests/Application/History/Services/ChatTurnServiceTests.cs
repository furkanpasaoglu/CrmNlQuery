using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.History.Services;

/// <summary>
/// Tests how the ask pipeline meets chats: a chat is born with its first answer and follow-ups stay in it.
/// </summary>
public class ChatTurnServiceTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>";

    private sealed class NoExecutor : IQueryExecutor
    {
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => throw new InvalidOperationException("Sohbet akışı CRM'e gitmez.");
    }

    private readonly MemoryHistoryStore _store = new MemoryHistoryStore();
    private readonly ChatTurnService _turns;
    private readonly ChatService _chats;

    public ChatTurnServiceTests()
    {
        _chats = new ChatService(_store);
        var context = new ConversationContext(_store, () => TimeSpan.FromMinutes(30));
        _turns = new ChatTurnService(_chats, context, new ResultService(_store, TestMetadata.Create(), new NoExecutor()));
    }

    private static AnswerOutcome Answer(string? basedOn = null)
        => new AnswerOutcome("account", "Showing Account", Fetch, new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()), false) { BasedOn = basedOn };

    private async Task<AnswerOutcome> Ask(Guid owner, string question, string? chatId, bool follow = false)
    {
        var turn = await _turns.PrepareAsync(owner, chatId);
        return await _turns.CompleteAsync(Answer(follow ? "x" : null), owner, question, turn);
    }

    /// <summary>
    /// Verifies that an aborted first question opens no chat and saves no result.
    /// </summary>
    [Fact]
    public async Task AnAbortedFirstQuestion_OpensNoChat_AndSavesNoResult()
    {
        var me = Guid.NewGuid();
        var turn = await _turns.PrepareAsync(me, null);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _turns.CompleteAsync(Answer(), me, "gizli soru", turn, aborted.Token));

        Assert.Equal(0, (await _chats.CountsAsync(me)).Total);
        Assert.Empty((await _chats.ListAsync(me, null, null, null, null)).Items);
    }

    /// <summary>
    /// Verifies that an aborted follow up leaves the chat as it was so it is never used as the previous question.
    /// </summary>
    [Fact]
    public async Task AnAbortedFollowUp_LeavesTheChatAsItWas_SoItIsNeverUsedAsThePreviousQuestion()
    {
        var me = Guid.NewGuid();
        var first = await Ask(me, "ilk soru", null);
        var turn = await _turns.PrepareAsync(me, first.ChatId);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _turns.CompleteAsync(Answer("ilk soru"), me, "durdurulan devam", turn, aborted.Token));

        var next = await _turns.PrepareAsync(me, first.ChatId);
        Assert.Equal("ilk soru", next.Previous!.Query.Question);
        Assert.Equal(1, (await _chats.CountsAsync(me)).Total);
    }

    /// <summary>
    /// Verifies that the first answer opens a new chat named after the question.
    /// </summary>
    [Fact]
    public async Task TheFirstAnswer_OpensANewChat_NamedAfterTheQuestion()
    {
        var me = Guid.NewGuid();

        var answer = await Ask(me, "Londra'daki firmaları getir", null);

        Assert.NotNull(answer.ChatId);
        Assert.NotNull(answer.ResultId);
        var chat = await _store.GetConversationAsync(me, answer.ChatId!);
        Assert.Equal("Londra'daki firmaları getir", chat!.Title);
        Assert.Equal(answer.ChatId, (await _store.GetAsync(me, answer.ResultId!))!.ConversationId);
    }

    /// <summary>
    /// Verifies that a follow up in the same chat gets the chats last question as context and becomes its child.
    /// </summary>
    [Fact]
    public async Task AFollowUpInTheSameChat_GetsTheChatsLastQuestionAsContext_AndBecomesItsChild()
    {
        var me = Guid.NewGuid();
        var first = await Ask(me, "ilk soru", null);

        var turn = await _turns.PrepareAsync(me, first.ChatId);
        Assert.Equal("ilk soru", turn.Previous!.Query.Question);
        Assert.Equal(first.ChatId, turn.ChatId);

        var second = await _turns.CompleteAsync(Answer("ilk soru"), me, "bunlardan 50 binin üstündekiler", turn);

        Assert.Equal(first.ChatId, second.ChatId);
        Assert.Equal(first.ResultId, (await _store.GetAsync(me, second.ResultId!))!.ParentId);
        Assert.Equal(2, (await _store.ListMessagesAsync(me, first.ChatId!, 10, null)).Items.Count);
        Assert.Equal(1, (await _store.CountConversationsAsync(me)).Total);
    }

    /// <summary>
    /// Verifies that a new question in the same chat does not become a child but stays in the chat.
    /// </summary>
    [Fact]
    public async Task ANewQuestionInTheSameChat_DoesNotBecomeAChild_ButStaysInTheChat()
    {
        var me = Guid.NewGuid();
        var first = await Ask(me, "ilk soru", null);

        var second = await Ask(me, "tamamen başka konu", first.ChatId, follow: false);

        Assert.Equal(first.ChatId, second.ChatId);
        Assert.Null((await _store.GetAsync(me, second.ResultId!))!.ParentId);
    }

    /// <summary>
    /// Verifies that two chats never share context.
    /// </summary>
    [Fact]
    public async Task TwoChats_NeverShareContext()
    {
        var me = Guid.NewGuid();
        var a = await Ask(me, "A sorusu", null);
        var b = await Ask(me, "B sorusu", null);
        Assert.NotEqual(a.ChatId, b.ChatId);

        var inA = await _turns.PrepareAsync(me, a.ChatId);
        var inB = await _turns.PrepareAsync(me, b.ChatId);

        Assert.Equal("A sorusu", inA.Previous!.Query.Question);
        Assert.Equal("B sorusu", inB.Previous!.Query.Question);
    }

    /// <summary>
    /// Verifies that without a chat ID a new chat starts with no context.
    /// </summary>
    [Fact]
    public async Task WithoutAChatId_ANewChatStartsWithNoContext()
    {
        var me = Guid.NewGuid();
        await Ask(me, "eski soru", null);

        var turn = await _turns.PrepareAsync(me, null);

        Assert.Null(turn.ChatId);
        Assert.Null(turn.Previous);
        var answer = await _turns.CompleteAsync(Answer(), me, "yeni soru", turn);
        Assert.Equal(2, (await _store.CountConversationsAsync(me)).Total);
        Assert.Equal("yeni soru", (await _store.GetConversationAsync(me, answer.ChatId!))!.Title);
    }

    /// <summary>
    /// Verifies that someone elses chat ID is ignored no context leaks and the chat is not touched.
    /// </summary>
    [Fact]
    public async Task SomeoneElsesChatId_IsIgnored_NoContextLeaks_AndTheChatIsNotTouched()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var secret = await Ask(owner, "gizli soru", null);

        var turn = await _turns.PrepareAsync(stranger, secret.ChatId);

        Assert.Null(turn.ChatId);
        Assert.Null(turn.Previous);
        var answer = await _turns.CompleteAsync(Answer(), stranger, "başkasının sorusu", turn);
        Assert.NotEqual(secret.ChatId, answer.ChatId);
        Assert.Single((await _store.ListMessagesAsync(owner, secret.ChatId!, 10, null)).Items);
    }



}
