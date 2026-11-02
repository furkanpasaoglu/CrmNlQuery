using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Application.Glossary.Exceptions;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Business glossary service: matches valid entries to questions, records counters, and adds or updates entries with validation, uniqueness and audit.
/// </summary>
public sealed class GlossaryService
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(15);

    private readonly IGlossaryStore _store;
    private readonly IMetadataSource _metadata;
    private readonly Func<bool> _enabled;
    private readonly Func<string, string, string, string, CancellationToken, Task>? _audit;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<string, DateTimeOffset> _warned = new Dictionary<string, DateTimeOffset>();
    private IReadOnlyList<GlossaryEntry>? _cache;
    private DateTimeOffset _cachedAt;

    public GlossaryService(IGlossaryStore store, IMetadataSource metadata, Func<bool> enabled, Func<string, string, string, string, CancellationToken, Task>? audit = null,
        ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _metadata = metadata;
        _enabled = enabled;
        _audit = audit;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _enabled();
    public bool Persistent => _store.Persistent;

    /// <summary>
    /// Clears the cached glossary entries so changes take effect.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate) { _cache = null; }
    }

    private async Task<IReadOnlyList<GlossaryEntry>> EntriesAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_cache is not null && _clock() - _cachedAt < CacheTime) { return _cache; }
        }
        var list = await _store.ListAsync(ct);
        lock (_gate)
        {
            _cache = list;
            _cachedAt = _clock();
        }
        return list;
    }

    /// <summary>
    /// Returns enabled and valid entries matching the question, or none when the feature is off or empty.
    /// </summary>
    public async Task<IReadOnlyList<GlossaryHit>> MatchAsync(string question, CancellationToken ct = default)
    {
        if (!_enabled()) { return Array.Empty<GlossaryHit>(); }
        var entries = await EntriesAsync(ct);
        if (entries.Count == 0) { return Array.Empty<GlossaryHit>(); }
        var matched = GlossaryMatcher.Match(question, entries);
        if (matched.Count == 0) { return matched; }

        var valid = new List<GlossaryHit>();
        foreach (var hit in matched)
        {
            var check = await GlossaryValidator.CheckAsync(hit.Entry, _metadata, ct);
            if (check.Ok) { valid.Add(hit); }
            else { WarnBroken(hit.Entry, check.Reason); }
        }
        return valid;
    }

    private void WarnBroken(GlossaryEntry entry, string? reason)
    {
        var key = entry.Id + "|" + reason;
        lock (_gate)
        {
            if (_warned.TryGetValue(key, out var at) && _clock() - at < TimeSpan.FromHours(1)) { return; }
            _warned[key] = _clock();
        }
        _logger?.LogWarning("Glossary entry {Entry} ({Term}) is broken ({Reason}) and is left out of questions until it is fixed in the Business glossary page.", entry.Id, entry.Terms.FirstOrDefault()?.Text, reason);
    }

    /// <summary>
    /// Records per-entry counters for applied and unapplied glossary findings at the end of a question; errors never break the question.
    /// </summary>
    public async Task RecordAsync(IReadOnlyList<GlossaryFinding> findings, CancellationToken ct = default)
    {
        foreach (var f in findings)
        {
            try { await _store.RecordAsync(f.Hit.Entry.Id, f.Applied, _clock(), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger?.LogWarning(ex, "Could not record glossary statistics for {Entry}", f.Hit.Entry.Id); }
        }
    }

    private async Task<GlossarySaveResult?> EquivalentTermAsync(GlossaryEntry entry, CancellationToken ct)
    {
        foreach (var other in await _store.ListAsync(ct))
        {
            if (other.Id == entry.Id) { continue; }
            foreach (var mine in entry.Terms)
            {
                var clash = other.Terms.FirstOrDefault(t => GlossaryMatcher.Equivalent(mine.Text, t.Text));
                if (clash is not null) { return new GlossarySaveResult(GlossarySaveStatus.Duplicate, mine.Text, other.Id); }
            }
        }
        return null;
    }

    /// <summary>
    /// Adds a glossary entry after validating against the filtered schema, checking term uniqueness and writing an audit record.
    /// </summary>
    public async Task<GlossarySaveResult> AddAsync(GlossaryEntry draft, string actor, CancellationToken ct = default)
    {
        var now = _clock();
        var entry = draft with { Id = Guid.NewGuid().ToString("N"), CreatedBy = actor, CreatedAt = now, ModifiedBy = actor, ModifiedAt = now };
        var check = await GlossaryValidator.CheckAsync(entry, _metadata, ct);
        if (!check.Ok) { return new GlossarySaveResult(GlossarySaveStatus.Invalid, check.Reason); }
        var equivalent = await EquivalentTermAsync(entry, ct);
        if (equivalent is not null) { return equivalent; }
        try { await _store.AddAsync(entry, ct); }
        catch (GlossaryDuplicateTermException ex) { return new GlossarySaveResult(GlossarySaveStatus.Duplicate, ex.Term, ex.OtherEntryId); }
        Invalidate();
        await Audit(actor, "glossary.create", entry, ct);
        return new GlossarySaveResult(GlossarySaveStatus.Saved, EntryId: entry.Id);
    }

    /// <summary>
    /// Updates a glossary entry with the same validation, uniqueness and audit rules.
    /// </summary>
    public async Task<GlossarySaveResult> UpdateAsync(GlossaryEntry edited, string actor, CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(edited.Id, ct);
        if (existing is null) { return new GlossarySaveResult(GlossarySaveStatus.NotFound); }
        var entry = edited with { CreatedBy = existing.CreatedBy, CreatedAt = existing.CreatedAt, ModifiedBy = actor, ModifiedAt = _clock() };
        var check = await GlossaryValidator.CheckAsync(entry, _metadata, ct);
        if (!check.Ok) { return new GlossarySaveResult(GlossarySaveStatus.Invalid, check.Reason); }
        var equivalent = await EquivalentTermAsync(entry, ct);
        if (equivalent is not null) { return equivalent; }
        try { await _store.UpdateAsync(entry, ct); }
        catch (GlossaryDuplicateTermException ex) { return new GlossarySaveResult(GlossarySaveStatus.Duplicate, ex.Term, ex.OtherEntryId); }
        Invalidate();
        await Audit(actor, entry.Enabled == existing.Enabled ? "glossary.update" : entry.Enabled ? "glossary.enable" : "glossary.disable", entry, ct);
        return new GlossarySaveResult(GlossarySaveStatus.Saved, EntryId: entry.Id);
    }

    /// <summary>
    /// Deletes a glossary entry and audits the deletion.
    /// </summary>
    public async Task<bool> DeleteAsync(string id, string actor, CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(id, ct);
        if (existing is null || !await _store.DeleteAsync(id, ct)) { return false; }
        Invalidate();
        await Audit(actor, "glossary.delete", existing, ct);
        return true;
    }

    /// <summary>
    /// Returns all business glossary entries from the glossary store.
    /// </summary>
    public Task<IReadOnlyList<GlossaryEntry>> ListAsync(CancellationToken ct = default) => _store.ListAsync(ct);

    /// <summary>
    /// Returns the usage statistics of glossary entries.
    /// </summary>
    public Task<IReadOnlyList<GlossaryStats>> StatsAsync(CancellationToken ct = default) => _store.StatsAsync(ct);

    private async Task Audit(string actor, string action, GlossaryEntry entry, CancellationToken ct)
    {
        if (_audit is null) { return; }
        await _audit(actor, action, entry.Id, $"{entry.Kind}: {string.Join(", ", entry.Terms.Select(t => t.Text))} ({entry.Entity})", ct);
    }
}
