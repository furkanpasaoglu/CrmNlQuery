using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Planning;

/// <summary>
/// Caches successful plans for the same question, records, columns and metadata, never storing row data.
/// </summary>
public sealed class CachingPlanGenerator : IPlanGenerator
{
    private readonly IPlanGenerator _inner;
    private readonly Func<TimeSpan> _ttl;
    private readonly int _capacity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<string, (DateTimeOffset At, PlanOutcome Outcome)> _entries = new Dictionary<string, (DateTimeOffset, PlanOutcome)>(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task<string>>? _policyVersion;
    private long _hits;
    private long _misses;

    public CachingPlanGenerator(IPlanGenerator inner, TimeSpan ttl, int capacity = 500, Func<DateTimeOffset>? clock = null, Func<CancellationToken, Task<string>>? policyVersion = null)
        : this(inner, () => ttl, capacity, clock, policyVersion)
    {
    }

    public CachingPlanGenerator(IPlanGenerator inner, Func<TimeSpan> ttl, int capacity = 500, Func<DateTimeOffset>? clock = null, Func<CancellationToken, Task<string>>? policyVersion = null)
    {
        _inner = inner;
        _ttl = ttl;
        _capacity = Math.Max(1, capacity);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _policyVersion = policyVersion;
    }

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>
    /// Returns a cached plan when available, otherwise generates, caches and returns one.
    /// </summary>
    public async Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
    {
        var ttl = _ttl();
        if (ttl <= TimeSpan.Zero)
        {
            return await _inner.GenerateAsync(request, ct);
        }
        var key = KeyFor(request) + (_policyVersion is null ? "" : "|" + await _policyVersion(ct));
        var now = _clock();

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                if (now - cached.At < ttl)
                {
                    Interlocked.Increment(ref _hits);
                    return cached.Outcome;
                }
                _entries.Remove(key);
            }
        }

        Interlocked.Increment(ref _misses);
        var outcome = await _inner.GenerateAsync(request, ct);
        if (outcome.Success)
        {
            lock (_gate)
            {
                while (_entries.Count >= _capacity)
                {
                    _entries.Remove(_entries.OrderBy(e => e.Value.At).First().Key);
                }
                _entries[key] = (now, outcome);
            }
        }
        return outcome;
    }

    private static string KeyFor(PlanRequest request)
    {
        const char Sep = '\u001f';
        var sb = new StringBuilder();
        sb.Append(CollapseSpaces(NameNormalizer.Fold(request.Question))).Append(Sep);
        sb.Append(request.Aggregate ? "aggregate" : string.Join(",", request.Columns)).Append(Sep);
        foreach (var r in request.Resolved.OrderBy(r => r.Entity, StringComparer.Ordinal).ThenBy(r => r.Id))
        {
            sb.Append(r.Mention).Append('|').Append(r.Entity).Append('|').Append(r.Id).Append(Sep);
        }
        if (request.Knowledge is { Count: > 0 })
        {
            sb.Append("knowledge").Append(Sep);
            foreach (var note in request.Knowledge)
            {
                sb.Append(note).Append(Sep);
            }
        }
        if (request.Glossary is { Count: > 0 })
        {
            sb.Append("glossary").Append(Sep);
            foreach (var hit in request.Glossary)
            {
                sb.Append(hit.Entry.Id).Append(':').Append(hit.Entry.ModifiedAt.UtcTicks).Append(Sep);
            }
        }
        if (request.Step is { } step)
        {
            sb.Append("step").Append(step.UsesStep?.ToString() ?? "-").Append(step.ProducesIds ? "P" : "-");
            foreach (var entity in step.IdEntities.OrderBy(x => x, StringComparer.Ordinal))
            {
                sb.Append('|').Append(entity);
            }
            sb.Append(Sep);
        }
        sb.Append('#');
        foreach (var e in request.Entities.OrderBy(e => e.LogicalName, StringComparer.Ordinal))
        {
            AppendEntity(sb, e, Sep);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static void AppendEntity(StringBuilder sb, EntityInfo e, char sep)
    {
        sb.Append(e.LogicalName).Append('|').Append(e.EntitySetName).Append('|').Append(e.Label).Append('|')
            .Append(e.PrimaryIdAttribute).Append('|').Append(e.PrimaryNameAttribute).Append(sep);
        foreach (var a in e.Attributes.OrderBy(a => a.LogicalName, StringComparer.Ordinal))
        {
            sb.Append(a.LogicalName).Append(':').Append(a.AttributeType).Append(':').Append(a.Label);
            foreach (var o in a.Options.OrderBy(o => o.Value))
            {
                sb.Append('/').Append(o.Value).Append('=').Append(o.Label);
            }
            sb.Append(sep);
        }
        foreach (var r in e.Relationships.OrderBy(r => r.SchemaName, StringComparer.Ordinal))
        {
            sb.Append(r.ReferencingEntity).Append('.').Append(r.ReferencingAttribute).Append("->")
                .Append(r.ReferencedEntity).Append('.').Append(r.ReferencedAttribute).Append(sep);
        }
        foreach (var m in e.ManyToMany.OrderBy(m => m.SchemaName, StringComparer.Ordinal))
        {
            sb.Append("m2m:").Append(m.SchemaName).Append('|').Append(m.IntersectEntity).Append('|').Append(m.Entity1).Append('.').Append(m.Entity1Attribute)
                .Append('|').Append(m.Entity2).Append('.').Append(m.Entity2Attribute).Append(sep);
        }
        sb.Append('#');
    }

    /// <summary>
    /// Collapses repeated whitespace in a string for stable cache keys.
    /// </summary>
    internal static string CollapseSpaces(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace)
                {
                    sb.Append(' ');
                }
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
