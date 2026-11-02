using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Application.Create.Services;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Create;

/// <summary>
/// In-memory draft store with a limited lifetime and per-user cap.
/// </summary>
public sealed class MemoryCreateDraftStore : ICreateDraftStore
{
    private const int PerOwnerLimit = 5;

    private readonly object _gate = new object();
    private readonly Dictionary<Guid, CreateDraft> _drafts = new Dictionary<Guid, CreateDraft>();
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _ttl;

    public MemoryCreateDraftStore(Func<DateTimeOffset>? clock = null, TimeSpan? ttl = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? TimeSpan.FromMinutes(20);
    }

    /// <summary>
    /// Saves a creation draft in memory after sweeping expired ones, keeping at most five pending drafts per user and dropping the oldest.
    /// </summary>
    public void Save(CreateDraft draft)
    {
        lock (_gate)
        {
            Sweep();
            _drafts[draft.Id] = draft;
            var mine = _drafts.Values.Where(d => d.Owner == draft.Owner).OrderBy(d => d.CreatedAt).ToList();
            foreach (var extra in mine.Take(Math.Max(0, mine.Count - PerOwnerLimit)))
            {
                _drafts.Remove(extra.Id);
            }
        }
    }

    /// <summary>
    /// Returns a draft only for its owner, or null.
    /// </summary>
    public CreateDraft? Get(Guid id, Guid owner)
    {
        lock (_gate)
        {
            Sweep();
            return _drafts.TryGetValue(id, out var draft) && draft.Owner == owner ? draft : null;
        }
    }

    /// <summary>
    /// Removes the creation draft with the given id from the in-memory store under its lock.
    /// </summary>
    public void Remove(Guid id)
    {
        lock (_gate)
        {
            _drafts.Remove(id);
        }
    }

    private void Sweep()
    {
        var now = _clock();
        foreach (var old in _drafts.Values.Where(d => now - d.CreatedAt > _ttl).Select(d => d.Id).ToList())
        {
            _drafts.Remove(old);
        }
    }
}
