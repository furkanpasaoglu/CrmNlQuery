using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Models;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Applies chat session rules such as titles, pinning and ownership, delegating storage to the history store.
/// </summary>
public sealed class ChatService
{
    public const int MaxTitleLength = 60;
    public const int AutoTitleLength = 40;
    public const int DefaultListTake = 30;
    public const int MaxListTake = 100;
    public const int DefaultMessageTake = 20;
    public const int MaxMessageTake = 50;
    public const int MaxSearchLength = 100;
    public const string EmptyTitle = "New chat";

    private readonly IHistoryStore _store;

    public ChatService(IHistoryStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Lists the owner's chats with paging, search and pinned filtering.
    /// </summary>
    public async Task<ChatList> ListAsync(Guid owner, int? take, string? cursor, string? search, bool? pinned, CancellationToken ct = default)
    {
        var size = Math.Clamp(take ?? DefaultListTake, 1, MaxListTake);
        var page = await _store.ListConversationsAsync(owner, size, cursor, CleanSearch(search), pinned, ct);
        return new ChatList(page.Items.Select(c => new ChatSummary(c.Id, c.Title, c.Pinned, c.CreatedAt, c.UpdatedAt)).ToList(), page.NextCursor);
    }

    /// <summary>
    /// Returns a chat's messages, or null if the chat is missing or not the caller's.
    /// </summary>
    public async Task<ChatMessages?> MessagesAsync(Guid owner, string chatId, int? take, string? cursor, CancellationToken ct = default)
    {
        if (await _store.GetConversationAsync(owner, chatId, ct) is null)
        {
            return null;
        }
        var size = Math.Clamp(take ?? DefaultMessageTake, 1, MaxMessageTake);
        var page = await _store.ListMessagesAsync(owner, chatId, size, cursor, ct);
        return new ChatMessages(page.Items.Select(e => new ChatMessage(e.Id, e.Question, e.Entity, e.CreatedAt)).ToList(), page.NextCursor);
    }

    /// <summary>
    /// Returns the owner's conversation counts by delegating to the history store.
    /// </summary>
    public Task<ConversationCounts> CountsAsync(Guid owner, CancellationToken ct = default)
    {
        return _store.CountConversationsAsync(owner, ct);
    }

    /// <summary>
    /// Renames a chat, returning false for an empty or invalid title or a chat that is missing or not the caller's.
    /// </summary>
    public async Task<bool> RenameAsync(Guid owner, string chatId, string? title, CancellationToken ct = default)
    {
        var clean = CleanTitle(title);
        if (clean.Length == 0)
        {
            return false;
        }
        return await _store.UpdateConversationAsync(owner, chatId, clean, null, ct);
    }

    /// <summary>
    /// Pins or unpins the owner's chat.
    /// </summary>
    public Task<bool> PinAsync(Guid owner, string chatId, bool pinned, CancellationToken ct = default)
    {
        return _store.UpdateConversationAsync(owner, chatId, null, pinned, ct);
    }

    /// <summary>
    /// Deletes one of the owner's chats through the history store, returning whether it existed and was removed.
    /// </summary>
    public Task<bool> DeleteAsync(Guid owner, string chatId, CancellationToken ct = default)
    {
        return _store.DeleteConversationAsync(owner, chatId, ct);
    }

    /// <summary>
    /// Deletes all of the owner's chats, including pinned ones only if requested, and returns the number deleted.
    /// </summary>
    public Task<int> DeleteAllAsync(Guid owner, bool includePinned, CancellationToken ct = default)
    {
        return _store.DeleteAllConversationsAsync(owner, includePinned, ct);
    }

    /// <summary>
    /// Returns the given chat if it is the caller's, otherwise creates a new one titled from the first question.
    /// </summary>
    public async Task<string> EnsureAsync(Guid owner, string? chatId, string question, CancellationToken ct = default)
    {
        if (chatId is not null && await _store.GetConversationAsync(owner, chatId, ct) is { } existing)
        {
            return existing.Id;
        }
        return await _store.CreateConversationAsync(owner, AutoTitle(question), ct);
    }

    /// <summary>
    /// Checks whether the chat belongs to the caller before loading its context.
    /// </summary>
    public async Task<bool> OwnsAsync(Guid owner, string? chatId, CancellationToken ct = default)
    {
        return chatId is not null && await _store.GetConversationAsync(owner, chatId, ct) is not null;
    }

    /// <summary>
    /// Derives a chat title from the first question.
    /// </summary>
    public static string AutoTitle(string? question)
    {
        var clean = PromptText.Clean(question, AutoTitleLength);
        return clean.Length == 0 ? EmptyTitle : clean;
    }

    /// <summary>
    /// Cleans and normalizes a user-supplied chat title.
    /// </summary>
    public static string CleanTitle(string? title) => PromptText.Clean(title, MaxTitleLength);

    private static string? CleanSearch(string? search)
    {
        var clean = PromptText.Clean(search, MaxSearchLength);
        return clean.Length == 0 ? null : clean;
    }
}
