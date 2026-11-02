using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Application.Glossary.Exceptions;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Application.Glossary.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

/// <summary>
/// Keeps the business glossary in memory when no database is configured, in eval and in tests.
/// </summary>
public sealed class MemoryGlossaryStore : IGlossaryStore
{
    private readonly object _gate = new object();
    private readonly List<GlossaryEntry> _entries = new List<GlossaryEntry>();
    private readonly Dictionary<string, (int Applied, int NotApplied, DateTimeOffset? Last)> _stats = new Dictionary<string, (int, int, DateTimeOffset?)>();

    public bool Persistent => false;

    /// <summary>
    /// Returns all in-memory glossary entries ordered by creation time then id, read under the store lock.
    /// </summary>
    public Task<IReadOnlyList<GlossaryEntry>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<GlossaryEntry>>(_entries.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id, StringComparer.Ordinal).ToList()); }
    }

    /// <summary>
    /// Returns a glossary entry by id, or null.
    /// </summary>
    public Task<GlossaryEntry?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_entries.FirstOrDefault(e => e.Id == id)); }
    }

    private GlossaryDuplicateTermException? Duplicate(GlossaryEntry entry)
    {
        foreach (var term in entry.Terms)
        {
            var key = GlossaryTermKey.Of(term.Text);
            var other = _entries.FirstOrDefault(e => e.Id != entry.Id && e.Terms.Any(t => GlossaryTermKey.Of(t.Text) == key));
            if (other is not null) { return new GlossaryDuplicateTermException(term.Text, other.Id); }
        }
        var own = entry.Terms.Select(t => GlossaryTermKey.Of(t.Text)).ToList();
        var dup = own.GroupBy(k => k).FirstOrDefault(g => g.Count() > 1);
        return dup is null ? null : new GlossaryDuplicateTermException(dup.Key, entry.Id);
    }

    /// <summary>
    /// Adds a glossary entry to memory under the lock, throwing if one of its terms duplicates an existing entry.
    /// </summary>
    public Task AddAsync(GlossaryEntry entry, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var duplicate = Duplicate(entry);
            if (duplicate is not null) { throw duplicate; }
            _entries.Add(entry);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces a glossary entry in memory, returning false if it does not exist and throwing on a duplicate term.
    /// </summary>
    public Task<bool> UpdateAsync(GlossaryEntry entry, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var i = _entries.FindIndex(e => e.Id == entry.Id);
            if (i < 0) { return Task.FromResult(false); }
            var duplicate = Duplicate(entry);
            if (duplicate is not null) { throw duplicate; }
            _entries[i] = entry;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Removes a glossary entry and its usage counters from memory, returning whether it existed.
    /// </summary>
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _stats.Remove(id);
            return Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0);
        }
    }

    /// <summary>
    /// Records whether a glossary entry was applied on a day.
    /// </summary>
    public Task RecordAsync(string entryId, bool applied, DateTimeOffset at, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var s = _stats.TryGetValue(entryId, out var current) ? current : (0, 0, null);
            _stats[entryId] = applied ? (s.Item1 + 1, s.Item2, s.Item3) : (s.Item1, s.Item2 + 1, at);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the usage counters per glossary entry.
    /// </summary>
    public Task<IReadOnlyList<GlossaryStats>> StatsAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<GlossaryStats>>(_stats.Select(kv => new GlossaryStats(kv.Key, kv.Value.Applied, kv.Value.NotApplied, kv.Value.Last)).ToList()); }
    }
}
