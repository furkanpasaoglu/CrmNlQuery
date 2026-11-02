using System.Text.RegularExpressions;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Knowledge.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

/// <summary>
/// In-memory knowledge store used when no database is configured and in tests; word search ignores case and Turkish letter differences.
/// </summary>
public sealed class MemoryKnowledgeStore : IKnowledgeStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, KnowledgeDocument> _documents = new Dictionary<Guid, KnowledgeDocument>();
    private readonly Dictionary<Guid, IReadOnlyList<KnowledgeChunk>> _chunks = new Dictionary<Guid, IReadOnlyList<KnowledgeChunk>>();

    /// <summary>
    /// Returns all in-memory knowledge documents, most recently updated first, read under the store lock.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeDocument>>(_documents.Values.OrderByDescending(d => d.UpdatedAt).ToList());
        }
    }

    /// <summary>
    /// Returns a knowledge document by id, or null.
    /// </summary>
    public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(id, out var d) ? d : null);
        }
    }

    /// <summary>
    /// Saves a document together with its chunks, replacing any previous version.
    /// </summary>
    public Task SaveAsync(KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _documents[document.Id] = document;
            _chunks[document.Id] = chunks.ToList();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Enables or disables a document and records who changed it and when.
    /// </summary>
    public Task SetEnabledAsync(Guid id, bool enabled, string actor, DateTimeOffset at, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(id, out var d))
            {
                _documents[id] = d with { Enabled = enabled, UpdatedBy = actor, UpdatedAt = at };
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a knowledge document and its chunks from the in-memory store.
    /// </summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _documents.Remove(id);
            _chunks.Remove(id);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the chunks closest to the embedding vector, up to the requested count.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeSnippet>> SearchByVectorAsync(float[] embedding, int take, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var found = Active()
                .Select(x => new KnowledgeSnippet(x.Doc.Id, x.Chunk.Index, x.Doc.Title, x.Chunk.Text, Cosine(embedding, x.Chunk.Embedding), "vector"))
                .OrderByDescending(s => s.Score)
                .Take(take)
                .ToList();
            return Task.FromResult<IReadOnlyList<KnowledgeSnippet>>(found);
        }
    }

    /// <summary>
    /// Returns chunks matching the question's words, up to the requested count.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeSnippet>> SearchByTextAsync(string question, int take, CancellationToken ct = default)
    {
        var words = Words(question);
        lock (_gate)
        {
            var found = Active()
                .Select(x =>
                {
                    var text = new HashSet<string>(DocumentWords(x.Doc.Title + " " + x.Chunk.Text));
                    return new KnowledgeSnippet(x.Doc.Id, x.Chunk.Index, x.Doc.Title, x.Chunk.Text, words.Count(text.Contains), "text");
                })
                .Where(s => s.Score > 0)
                .OrderByDescending(s => s.Score)
                .Take(take)
                .ToList();
            return Task.FromResult<IReadOnlyList<KnowledgeSnippet>>(found);
        }
    }

    private IEnumerable<(KnowledgeDocument Doc, KnowledgeChunk Chunk)> Active()
    {
        foreach (var doc in _documents.Values.Where(d => d.Enabled))
        {
            foreach (var chunk in _chunks[doc.Id])
            {
                yield return (doc, chunk);
            }
        }
    }

    private static IEnumerable<string> DocumentWords(string text)
    {
        return Regex.Matches(NameNormalizer.Fold(text), @"[\p{L}\p{N}_]{3,}").Select(m => m.Value);
    }

    private static List<string> Words(string text)
    {
        return SearchWords.Extract(text).Select(NameNormalizer.Fold).ToList();
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
