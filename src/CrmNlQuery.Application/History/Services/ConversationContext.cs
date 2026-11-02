using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Models;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Loads the previous answer on the server for follow-up questions, so the client sends only a result id.
/// </summary>
public sealed class ConversationContext
{
    private readonly IHistoryStore _history;
    private readonly Func<TimeSpan> _maxAge;
    private readonly Func<DateTimeOffset> _clock;

    public ConversationContext(IHistoryStore history, Func<TimeSpan> maxAge, Func<DateTimeOffset>? clock = null)
    {
        _history = history;
        _maxAge = maxAge;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Loads the previous answer from the chat's last answer, only if it belongs to the caller.
    /// </summary>
    public async Task<ConversationPrevious?> LoadLastAsync(Guid owner, string? chatId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return null;
        }
        var entry = await _history.LastEntryAsync(owner, chatId.Trim(), ct);
        if (entry is null || entry.Kind is not null || _clock() - entry.CreatedAt > _maxAge())
        {
            return null;
        }
        return new ConversationPrevious(new PreviousQuery(entry.Question, entry.Entity, entry.FetchXml), entry.Id, entry.ConversationId);
    }

    /// <summary>
    /// Loads a previous answer by result id, optionally ignoring its age when the user deliberately continues.
    /// </summary>
    public async Task<ConversationPrevious?> LoadAsync(Guid owner, string? resultId, bool ignoreAge = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resultId))
        {
            return null;
        }
        var entry = await _history.GetAsync(owner, resultId.Trim(), ct);
        if (entry is null || entry.Kind is not null)
        {
            return null;
        }
        if (!ignoreAge && _clock() - entry.CreatedAt > _maxAge())
        {
            return null;
        }
        return new ConversationPrevious(new PreviousQuery(entry.Question, entry.Entity, entry.FetchXml), entry.Id, entry.ConversationId);
    }
}
