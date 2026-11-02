using CrmNlQuery.Domain.History.Entities;
using CrmNlQuery.Application.History.Models;

namespace CrmNlQuery.Application.History.Abstractions;

/// <summary>
/// Stores chat history and conversations; every operation is bound to the owner and others' or unknown ids behave as not found.
/// </summary>
public interface IHistoryStore
{
    /// <summary>
    /// Adds a history record for the owner, optionally as a follow-up to a previous record and inside a given conversation.
    /// </summary>
    Task<string> AddAsync(Guid owner, string question, string entity, string fetchXml, string? parentId = null, string? conversationId = null, CancellationToken ct = default, bool adminViewable = false,
        int? steps = null, int? durationMs = null, string? kind = null, string? payload = null);

    /// <summary>
    /// Lists the owner's records flagged as admin-viewable, newest first, with no separate copy kept.
    /// </summary>
    Task<IReadOnlyList<ViewableQuestion>> ListViewableQuestionsAsync(Guid owner, int take, CancellationToken ct = default);

    /// <summary>
    /// Returns the owner's history record by id, or null if missing or not theirs.
    /// </summary>
    Task<HistoryEntry?> GetAsync(Guid owner, string id, CancellationToken ct = default);

    /// <summary>
    /// Deletes the owner's history record by id.
    /// </summary>
    Task<bool> DeleteAsync(Guid owner, string id, CancellationToken ct = default);

    /// <summary>
    /// Deletes records older than the cutoff that are not in a pinned conversation, and conversations left empty.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    /// <summary>
    /// Creates a conversation for the owner with a non-empty title.
    /// </summary>
    Task<string> CreateConversationAsync(Guid owner, string title, CancellationToken ct = default);

    /// <summary>
    /// Returns the owner's conversation by id, or null if missing or not theirs.
    /// </summary>
    Task<Conversation?> GetConversationAsync(Guid owner, string id, CancellationToken ct = default);

    /// <summary>
    /// Lists the owner's conversations with paging, search and pinned filtering, pinned first then newest updated.
    /// </summary>
    Task<ConversationPage> ListConversationsAsync(Guid owner, int take, string? cursor, string? search, bool? pinned, CancellationToken ct = default);

    /// <summary>
    /// Updates a conversation's title and/or pinned flag for its owner, returning false if not found or not owned.
    /// </summary>
    Task<bool> UpdateConversationAsync(Guid owner, string id, string? title, bool? pinned, CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation and its records for its owner.
    /// </summary>
    Task<bool> DeleteConversationAsync(Guid owner, string id, CancellationToken ct = default);

    /// <summary>
    /// Deletes all of the owner's conversations and records, keeping pinned ones unless includePinned is true.
    /// </summary>
    Task<int> DeleteAllConversationsAsync(Guid owner, bool includePinned, CancellationToken ct = default);

    /// <summary>
    /// Returns the owner's conversation counts, such as total and pinned chats, from the history store.
    /// </summary>
    Task<ConversationCounts> CountConversationsAsync(Guid owner, CancellationToken ct = default);

    /// <summary>
    /// Lists a conversation's messages newest page first, returned oldest to newest within the page, using a cursor.
    /// </summary>
    Task<MessagePage> ListMessagesAsync(Guid owner, string conversationId, int take, string? cursor, CancellationToken ct = default);

    /// <summary>
    /// Returns the newest record of a conversation, or null if missing, not owned or empty.
    /// </summary>
    Task<HistoryEntry?> LastEntryAsync(Guid owner, string conversationId, CancellationToken ct = default);
}
