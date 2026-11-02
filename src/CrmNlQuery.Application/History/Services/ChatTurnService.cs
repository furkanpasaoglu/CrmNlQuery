using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Models;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Connects the ask pipeline to chats: checks chat ownership and loads follow-up context before a question, stores the answer after.
/// </summary>
public sealed class ChatTurnService
{
    private readonly ChatService _chats;
    private readonly ConversationContext _context;
    private readonly ResultService _results;

    public ChatTurnService(ChatService chats, ConversationContext context, ResultService results)
    {
        _chats = chats;
        _context = context;
        _results = results;
    }

    /// <summary>
    /// Checks that the chat belongs to the caller, treating a foreign or missing chat as a new chat, and loads the follow-up context.
    /// </summary>
    public async Task<ChatTurn> PrepareAsync(Guid owner, string? chatId, CancellationToken ct = default)
    {
        var ownChat = await _chats.OwnsAsync(owner, chatId, ct) ? chatId : null;
        var previous = ownChat is not null ? await _context.LoadLastAsync(owner, ownChat, ct) : null;
        return new ChatTurn(ownChat, previous);
    }

    /// <summary>
    /// Stores a customer summary in the chat as a history record that is recomputed when reopened.
    /// </summary>
    public async Task<SummaryOutcome> CompleteSummaryAsync(SummaryOutcome summary, Guid owner, string question, ChatTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;
        var chat = await _chats.EnsureAsync(owner, turn.ChatId, question, ct);
        return await _results.PublishSummaryAsync(summary, owner, question, chat, ct);
    }

    /// <summary>
    /// Stores a record-creation result in the chat as a read-only history record.
    /// </summary>
    public async Task<CrmNlQuery.Application.Create.Models.CreateOutcome> CompleteCreateAsync(CrmNlQuery.Application.Create.Models.CreateOutcome outcome, Guid owner, string question, ChatTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;
        var chat = await _chats.EnsureAsync(owner, turn.ChatId, question, ct);
        return await _results.PublishCreateAsync(outcome, owner, question, chat, ct);
    }

    /// <summary>
    /// Stores a report-finder result in the chat as a read-only history record.
    /// </summary>
    public async Task<ReportOutcome> CompleteReportAsync(ReportOutcome report, Guid owner, string question, ChatTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;
        var chat = await _chats.EnsureAsync(owner, turn.ChatId, question, ct);
        return await _results.PublishReportAsync(report, owner, question, chat, ct);
    }

    /// <summary>
    /// Stores an email draft in the chat as a history record holding only the placeholder template, refilled when reopened.
    /// </summary>
    public async Task<DraftOutcome> CompleteDraftAsync(DraftOutcome draft, Guid owner, string question, ChatTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;
        var chat = await _chats.EnsureAsync(owner, turn.ChatId, question, ct);
        return await _results.PublishDraftAsync(draft, owner, question, chat, ct);
    }

    /// <summary>
    /// Stores an answer bound to its owner and returns the result id and chat id, linking follow-ups to the previous result.
    /// </summary>
    public async Task<AnswerOutcome> CompleteAsync(AnswerOutcome answer, Guid owner, string question, ChatTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;
        var parentId = answer.BasedOn is not null ? turn.Previous?.ResultId : null;
        var chat = await _chats.EnsureAsync(owner, turn.ChatId, question, ct);
        return await _results.PublishAsync(answer, owner, question, parentId, chat, ct);
    }
}
