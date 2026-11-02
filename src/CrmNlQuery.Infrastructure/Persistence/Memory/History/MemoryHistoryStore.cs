using System.Security.Cryptography;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.History.Entities;
using CrmNlQuery.Application.History.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.History;

/// <summary>
/// Keeps chat history in memory when no database is configured and in tests; contents are lost when the service restarts.
/// </summary>
public sealed class MemoryHistoryStore : IHistoryStore, CrmNlQuery.Application.Export.Abstractions.IQuestionPager
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _capacity;
    private readonly object _gate = new object();
    private readonly Dictionary<string, HistoryEntry> _entries = new Dictionary<string, HistoryEntry>();
    private readonly Dictionary<string, Conversation> _conversations = new Dictionary<string, Conversation>();
    private readonly HashSet<string> _viewable = new HashSet<string>(StringComparer.Ordinal);

    public MemoryHistoryStore(Func<DateTimeOffset>? clock = null, int capacity = 5000)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _capacity = capacity;
    }

    /// <summary>
    /// Adds a history entry for the owner and returns its new id.
    /// </summary>
    public Task<string> AddAsync(Guid owner, string question, string entity, string fetchXml, string? parentId = null, string? conversationId = null, CancellationToken ct = default, bool adminViewable = false,
        int? steps = null, int? durationMs = null, string? kind = null, string? payload = null)
    {
        var id = NewId();
        lock (_gate)
        {
            while (_entries.Count >= _capacity && _entries.Count > 0)
            {
                var oldest = _entries.OrderBy(e => e.Value.CreatedAt).First().Key;
                _entries.Remove(oldest);
            }
            var chat = FindConversation(owner, conversationId);
            var now = _clock();
            _entries[id] = new HistoryEntry(id, owner, question, entity, fetchXml, now, null, false, parentId, chat?.Id, steps, durationMs, kind, payload);
            if (adminViewable)
            {
                _viewable.Add(id);
            }
            if (chat is not null)
            {
                _conversations[chat.Id] = chat with { UpdatedAt = now };
            }
        }
        return Task.FromResult(id);
    }

    /// <summary>
    /// Checks whether a history entry still exists.
    /// </summary>
    public bool Exists(string id)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(id);
        }
    }

    /// <summary>
    /// Pages through flagged questions for export in time order using a time and id cursor.
    /// </summary>
    public Task<IReadOnlyList<CrmNlQuery.Application.Export.Models.QuestionPageItem>> PageQuestionsAsync(DateTimeOffset from, DateTimeOffset to, Guid? user,
        CrmNlQuery.Application.Export.Models.QuestionCursor? after, int size, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var page = _entries.Values
                .Where(e => _viewable.Contains(e.Id) && e.CreatedAt >= from && e.CreatedAt < to && (user is null || e.Owner == user))
                .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id, StringComparer.Ordinal)
                .Where(e => after is null || e.CreatedAt > after.At || (e.CreatedAt == after.At && string.CompareOrdinal(e.Id, after.Id) > 0))
                .Take(Math.Max(1, size))
                .Select(e => new CrmNlQuery.Application.Export.Models.QuestionPageItem(e.Id, e.Owner, e.CreatedAt, e.Question, e.Entity)).ToList();
            return Task.FromResult<IReadOnlyList<CrmNlQuery.Application.Export.Models.QuestionPageItem>>(page);
        }
    }

    /// <summary>
    /// Lists the owner questions that were flagged as viewable by administrators at ask time.
    /// </summary>
    public Task<IReadOnlyList<ViewableQuestion>> ListViewableQuestionsAsync(Guid owner, int take, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var list = _entries.Values
                .Where(e => e.Owner == owner && _viewable.Contains(e.Id))
                .OrderByDescending(e => e.CreatedAt)
                .Take(Math.Max(1, take))
                .Select(e => new ViewableQuestion(e.CreatedAt, e.Question, e.Entity))
                .ToList();
            return Task.FromResult<IReadOnlyList<ViewableQuestion>>(list);
        }
    }

    /// <summary>
    /// Returns a history entry only for its owner, or null.
    /// </summary>
    public Task<HistoryEntry?> GetAsync(Guid owner, string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Find(owner, id));
        }
    }

    /// <summary>
    /// Deletes a history entry only for its owner.
    /// </summary>
    public Task<bool> DeleteAsync(Guid owner, string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Find(owner, id) is not null && _entries.Remove(id));
        }
    }

    /// <summary>
    /// Deletes history entries older than the cutoff.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var old = _entries.Where(e => e.Value.CreatedAt < cutoff && !InPinnedConversation(e.Value)).Select(e => e.Key).ToList();
            foreach (var id in old)
            {
                _entries.Remove(id);
            }
            var empty = _conversations.Values
                .Where(c => c.UpdatedAt < cutoff && !c.Pinned && !_entries.Values.Any(e => e.ConversationId == c.Id))
                .Select(c => c.Id).ToList();
            foreach (var id in empty)
            {
                _conversations.Remove(id);
            }
            return Task.FromResult(old.Count);
        }
    }

    /// <summary>
    /// Creates a new conversation for the owner and returns its id.
    /// </summary>
    public Task<string> CreateConversationAsync(Guid owner, string title, CancellationToken ct = default)
    {
        var id = NewId();
        var now = _clock();
        lock (_gate)
        {
            _conversations[id] = new Conversation(id, owner, title, false, now, now);
        }
        return Task.FromResult(id);
    }

    /// <summary>
    /// Returns a conversation only for its owner, or null.
    /// </summary>
    public Task<Conversation?> GetConversationAsync(Guid owner, string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(FindConversation(owner, id));
        }
    }

    /// <summary>
    /// Lists the owner conversations with pinned first, optional search and cursor paging.
    /// </summary>
    public Task<ConversationPage> ListConversationsAsync(Guid owner, int take, string? cursor, string? search, bool? pinned, CancellationToken ct = default)
    {
        var size = Math.Max(0, take);
        var needle = (search ?? "").Trim();
        lock (_gate)
        {
            var query = _conversations.Values
                .Where(c => c.Owner == owner
                    && (pinned is null || c.Pinned == pinned)
                    && (needle.Length == 0 || c.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(c => c.Pinned).ThenByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Id, StringComparer.Ordinal)
                .AsEnumerable();
            if (HistoryCursor.TryReadConversation(cursor, out var cursorPinned, out var cursorAt, out var cursorId))
            {
                query = query.Where(c => IsAfter(c, cursorPinned, cursorAt, cursorId));
            }
            var items = query.Take(size + 1).ToList();
            var more = items.Count > size;
            var page = items.Take(size).ToList();
            var next = more && page.Count > 0 ? HistoryCursor.ForConversation(page[page.Count - 1].Pinned, page[page.Count - 1].UpdatedAt, page[page.Count - 1].Id) : null;
            return Task.FromResult(new ConversationPage(page, next));
        }
    }

    /// <summary>
    /// Updates only the given title or pinned flag of the owner conversation.
    /// </summary>
    public Task<bool> UpdateConversationAsync(Guid owner, string id, string? title, bool? pinned, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var chat = FindConversation(owner, id);
            if (chat is null)
            {
                return Task.FromResult(false);
            }
            _conversations[chat.Id] = chat with { Title = title ?? chat.Title, Pinned = pinned ?? chat.Pinned };
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Deletes a conversation and its entries only for its owner.
    /// </summary>
    public Task<bool> DeleteConversationAsync(Guid owner, string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var chat = FindConversation(owner, id);
            if (chat is null)
            {
                return Task.FromResult(false);
            }
            RemoveConversation(chat.Id);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Deletes all conversations of the owner, keeping pinned ones unless asked.
    /// </summary>
    public Task<int> DeleteAllConversationsAsync(Guid owner, bool includePinned, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var chats = _conversations.Values.Where(c => c.Owner == owner && (includePinned || !c.Pinned)).Select(c => c.Id).ToList();
            foreach (var id in chats)
            {
                RemoveConversation(id);
            }
            var legacy = _entries.Values.Where(e => e.Owner == owner && e.ConversationId is null).Select(e => e.Id).ToList();
            foreach (var id in legacy)
            {
                _entries.Remove(id);
            }
            return Task.FromResult(chats.Count);
        }
    }

    /// <summary>
    /// Counts the owner conversations in total and pinned.
    /// </summary>
    public Task<ConversationCounts> CountConversationsAsync(Guid owner, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var mine = _conversations.Values.Where(c => c.Owner == owner).ToList();
            return Task.FromResult(new ConversationCounts(mine.Count, mine.Count(c => c.Pinned)));
        }
    }

    /// <summary>
    /// Lists a conversation messages page by page for its owner.
    /// </summary>
    public Task<MessagePage> ListMessagesAsync(Guid owner, string conversationId, int take, string? cursor, CancellationToken ct = default)
    {
        var size = Math.Max(0, take);
        lock (_gate)
        {
            if (FindConversation(owner, conversationId) is null)
            {
                return Task.FromResult(new MessagePage(new List<HistoryEntry>(), null));
            }
            var query = _entries.Values
                .Where(e => e.Owner == owner && e.ConversationId == conversationId)
                .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .AsEnumerable();
            if (HistoryCursor.TryReadMessage(cursor, out var cursorAt, out var cursorId))
            {
                query = query.Where(e => e.CreatedAt < cursorAt || (e.CreatedAt == cursorAt && string.CompareOrdinal(e.Id, cursorId) < 0));
            }
            var items = query.Take(size + 1).ToList();
            var more = items.Count > size;
            var page = items.Take(size).ToList();
            var next = more && page.Count > 0 ? HistoryCursor.ForMessage(page[page.Count - 1].CreatedAt, page[page.Count - 1].Id) : null;
            page.Reverse();
            return Task.FromResult(new MessagePage(page, next));
        }
    }

    /// <summary>
    /// Returns the most recent entry of the owner conversation.
    /// </summary>
    public Task<HistoryEntry?> LastEntryAsync(Guid owner, string conversationId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (FindConversation(owner, conversationId) is null)
            {
                return Task.FromResult<HistoryEntry?>(null);
            }
            var last = _entries.Values
                .Where(e => e.Owner == owner && e.ConversationId == conversationId)
                .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            return Task.FromResult<HistoryEntry?>(last);
        }
    }

    private static string NewId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private Conversation? FindConversation(Guid owner, string? id)
    {
        return _conversations.TryGetValue(id ?? "", out var chat) && chat.Owner == owner ? chat : null;
    }

    private bool InPinnedConversation(HistoryEntry entry)
    {
        return entry.ConversationId is not null && _conversations.TryGetValue(entry.ConversationId, out var chat) && chat.Pinned;
    }

    private void RemoveConversation(string id)
    {
        _conversations.Remove(id);
        foreach (var entryId in _entries.Values.Where(e => e.ConversationId == id).Select(e => e.Id).ToList())
        {
            _entries.Remove(entryId);
        }
    }

    private static bool IsAfter(Conversation c, bool cursorPinned, DateTimeOffset cursorAt, string cursorId)
    {
        if (c.Pinned != cursorPinned)
        {
            return cursorPinned && !c.Pinned;
        }
        if (c.UpdatedAt != cursorAt)
        {
            return c.UpdatedAt < cursorAt;
        }
        return string.CompareOrdinal(c.Id, cursorId) < 0;
    }

    private HistoryEntry? Find(Guid owner, string id)
    {
        return _entries.TryGetValue(id ?? "", out var entry) && entry.Owner == owner ? entry : null;
    }
}
