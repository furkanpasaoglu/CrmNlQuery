using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Planning;

/// <summary>
/// Caches question analyses so a second round of the same user with identical inputs does not repeat the model call.
/// </summary>
public sealed class AnalysisCache
{
    private readonly Func<TimeSpan> _ttl;
    private readonly int _capacity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<string, (DateTimeOffset At, QuestionAnalysis Analysis)> _entries = new Dictionary<string, (DateTimeOffset, QuestionAnalysis)>(StringComparer.Ordinal);
    private long _hits;
    private long _misses;

    public AnalysisCache(Func<TimeSpan> ttl, int capacity = 500, Func<DateTimeOffset>? clock = null)
    {
        _ttl = ttl;
        _capacity = Math.Max(1, capacity);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>
    /// Returns a cached analysis for the key if present.
    /// </summary>
    public bool TryGet(string key, out QuestionAnalysis analysis)
    {
        analysis = null!;
        var ttl = _ttl();
        if (ttl <= TimeSpan.Zero)
        {
            return false;
        }
        var now = _clock();
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                if (now - cached.At < ttl)
                {
                    Interlocked.Increment(ref _hits);
                    analysis = cached.Analysis;
                    return true;
                }
                _entries.Remove(key);
            }
        }
        Interlocked.Increment(ref _misses);
        return false;
    }

    /// <summary>
    /// Stores an analysis in the cache under the key.
    /// </summary>
    public void Put(string key, QuestionAnalysis analysis)
    {
        if (_ttl() <= TimeSpan.Zero)
        {
            return;
        }
        var now = _clock();
        lock (_gate)
        {
            while (_entries.Count >= _capacity && !_entries.ContainsKey(key))
            {
                _entries.Remove(_entries.OrderBy(e => e.Value.At).First().Key);
            }
            _entries[key] = (now, analysis);
        }
    }

    /// <summary>
    /// Builds the cache key from the caller, question, entity list, knowledge and previous or open record context.
    /// </summary>
    public static string KeyFor(Caller caller, string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge,
        PreviousQuery? previous, ResolvedRecord? current, IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit>? glossary = null)
    {
        const char Sep = '\u001f';
        var sb = new StringBuilder();
        sb.Append(caller.IsEvaluation ? "evaluation" : caller.UserId.ToString("N")).Append(Sep);
        sb.Append(CachingPlanGenerator.CollapseSpaces(NameNormalizer.Fold(question))).Append(Sep);
        foreach (var e in entities)
        {
            sb.Append(e.LogicalName).Append('|').Append(e.EntitySetName).Append('|').Append(e.Label).Append(Sep);
        }
        sb.Append('#');
        foreach (var note in knowledge)
        {
            sb.Append(note).Append(Sep);
        }
        sb.Append('#');
        foreach (var hit in glossary ?? Array.Empty<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit>())
        {
            sb.Append(hit.Entry.Id).Append(':').Append(hit.Entry.ModifiedAt.UtcTicks).Append(Sep);
        }
        sb.Append('#');
        if (previous is not null)
        {
            sb.Append("previous").Append(Sep).Append(previous.Question).Append(Sep).Append(previous.Entity).Append(Sep).Append(previous.FetchXml).Append(Sep);
        }
        if (current is not null)
        {
            sb.Append("current").Append(Sep).Append(current.Entity).Append(Sep).Append(current.Id).Append(Sep).Append(current.Name).Append(Sep);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
