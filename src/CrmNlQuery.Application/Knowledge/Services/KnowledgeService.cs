using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Knowledge.Constants;
using CrmNlQuery.Domain.Knowledge.Entities;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Knowledge.Services;

/// <summary>
/// Application service implementing the knowledge rules.
/// </summary>
public sealed class KnowledgeService
{
    private readonly IKnowledgeStore _store;
    private readonly IEmbeddingClient _embeddings;
    private readonly Func<KnowledgeOptions> _options;
    private readonly Func<AuditEntry, CancellationToken, Task> _audit;
    private readonly Func<DateTimeOffset> _clock;

    public KnowledgeService(IKnowledgeStore store, IEmbeddingClient embeddings, KnowledgeOptions options,
        Func<AuditEntry, CancellationToken, Task> audit, Func<DateTimeOffset>? clock = null)
        : this(store, embeddings, () => options, audit, clock)
    {
    }

    public KnowledgeService(IKnowledgeStore store, IEmbeddingClient embeddings, Func<KnowledgeOptions> options,
        Func<AuditEntry, CancellationToken, Task> audit, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _embeddings = embeddings;
        _options = options;
        _audit = audit;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _options().Enabled;

    public bool Available => _store is not IKnowledgeAvailability availability || availability.IsAvailable;

    public string? UnavailableReason => (_store as IKnowledgeAvailability)?.Reason;

    private AccessResult? UnavailableResult()
        => Available ? null : AccessResult.Fail(AccessError.Unavailable, MessageCatalog.Now(Msg.AdmKbUnavailable, MessageCatalog.Args(("reason", UnavailableReason ?? ""))));

    /// <summary>
    /// Lists all knowledge-base documents.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(CancellationToken ct = default) => _store.ListAsync(ct);

    /// <summary>
    /// Fetches one knowledge-base document by id, or null if missing.
    /// </summary>
    public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct = default) => _store.GetAsync(id, ct);

    /// <summary>
    /// Creates a knowledge document for an authorized actor, validating title and body, and chunks and embeds it for search.
    /// </summary>
    public async Task<AccessResult> CreateAsync(AccessPrincipal actor, string? title, string? body, CancellationToken ct = default)
    {
        if (UnavailableResult() is { } unavailable)
        {
            return unavailable;
        }
        var invalid = Validate(title, body);
        if (invalid is not null)
        {
            return invalid;
        }
        var duplicate = await FindDuplicateAsync(null, title!, body!, ct);
        if (duplicate is not null)
        {
            return duplicate;
        }
        var document = new KnowledgeDocument(Guid.NewGuid(), title!.Trim(), body!.Trim(), true, actor.Account, _clock());
        var saved = await IndexAndSaveAsync(document, ct);
        if (!saved.Ok)
        {
            return saved;
        }
        await AuditAsync(actor, "doc.create", document, ct);
        return AccessResult.Created(document.Id);
    }

    /// <summary>
    /// Updates a document's title or body, re-embedding its chunks when content changes while keeping its enabled state.
    /// </summary>
    public async Task<AccessResult> UpdateAsync(AccessPrincipal actor, Guid id, string? title, string? body, CancellationToken ct = default)
    {
        if (UnavailableResult() is { } unavailable)
        {
            return unavailable;
        }
        var invalid = Validate(title, body);
        if (invalid is not null)
        {
            return invalid;
        }
        var existing = await _store.GetAsync(id, ct);
        if (existing is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmDocGone));
        }
        var duplicate = await FindDuplicateAsync(id, title!, body!, ct);
        if (duplicate is not null)
        {
            return duplicate;
        }
        var document = existing with { Title = title!.Trim(), Body = body!.Trim(), UpdatedBy = actor.Account, UpdatedAt = _clock() };
        var saved = await IndexAndSaveAsync(document, ct);
        if (!saved.Ok)
        {
            return saved;
        }
        await AuditAsync(actor, "doc.update", document, ct);
        return AccessResult.Success;
    }

    /// <summary>
    /// Enables or disables a knowledge document so it is included in or excluded from search.
    /// </summary>
    public async Task<AccessResult> SetEnabledAsync(AccessPrincipal actor, Guid id, bool enabled, CancellationToken ct = default)
    {
        if (UnavailableResult() is { } unavailable)
        {
            return unavailable;
        }
        var existing = await _store.GetAsync(id, ct);
        if (existing is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmDocGone));
        }
        await _store.SetEnabledAsync(id, enabled, actor.Account, _clock(), ct);
        await AuditAsync(actor, enabled ? "doc.enable" : "doc.disable", existing, ct);
        return AccessResult.Success;
    }

    /// <summary>
    /// Deletes a knowledge document and its chunks on behalf of an authorized actor.
    /// </summary>
    public async Task<AccessResult> DeleteAsync(AccessPrincipal actor, Guid id, CancellationToken ct = default)
    {
        if (UnavailableResult() is { } unavailable)
        {
            return unavailable;
        }
        var existing = await _store.GetAsync(id, ct);
        if (existing is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmDocGone));
        }
        await _store.DeleteAsync(id, ct);
        await AuditAsync(actor, "doc.delete", existing, ct);
        return AccessResult.Success;
    }

    /// <summary>
    /// Finds the most relevant chunks for a question by fusing semantic and keyword search, dropping low-similarity vector hits and capping by TopK and total MaxChars.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSnippet>> SearchAsync(string question, CancellationToken ct = default)
    {
        var options = _options();
        var trace = await TraceAsync(question, options.Candidates, ct);
        var result = new List<KnowledgeSnippet>();
        var chars = 0;
        foreach (var snippet in trace.Fused)
        {
            if (result.Count >= Math.Max(1, options.TopK) || (result.Count > 0 && chars + snippet.Text.Length > options.MaxChars))
            {
                break;
            }
            result.Add(snippet);
            chars += snippet.Text.Length;
        }
        return result;
    }

    /// <summary>
    /// Returns raw semantic and keyword candidate lists plus the fused ranking before TopK and MaxChars limits, for retrieval measurement and debugging.
    /// </summary>
    public async Task<KnowledgeSearchTrace> TraceAsync(string question, int candidates, CancellationToken ct = default)
    {
        if (!Available || string.IsNullOrWhiteSpace(question))
        {
            return new KnowledgeSearchTrace(new List<KnowledgeSnippet>(), new List<KnowledgeSnippet>(), new List<KnowledgeSnippet>());
        }
        var embedding = (await _embeddings.EmbedAsync(new[] { question }, ct)).Single();
        var options = _options();
        var take = Math.Max(1, candidates);

        var byVector = (await _store.SearchByVectorAsync(embedding, take, ct))
            .Where(s => s.Score >= options.MinSimilarity)
            .ToList();
        var byText = await _store.SearchByTextAsync(question, take, ct);

        var lookup = new Dictionary<(Guid, int), KnowledgeSnippet>();
        foreach (var s in byText.Concat(byVector))
        {
            lookup[(s.DocumentId, s.ChunkIndex)] = s;
        }
        var vectorKeys = byVector.Select(s => (s.DocumentId, s.ChunkIndex)).ToList();
        var textKeys = byText.Select(s => (s.DocumentId, s.ChunkIndex)).ToList();

        var fused = new List<KnowledgeSnippet>();
        foreach (var (key, score) in RankFusion.Fuse<(Guid, int)>(new IReadOnlyList<(Guid, int)>[] { vectorKeys, textKeys }))
        {
            var inVector = vectorKeys.Contains(key);
            var inText = textKeys.Contains(key);
            var source = inVector && inText ? "both" : inVector ? "vector" : "text";
            fused.Add(lookup[key] with { Score = score, Source = source });
        }
        return new KnowledgeSearchTrace(byVector, byText, fused);
    }

    private async Task<AccessResult> IndexAndSaveAsync(KnowledgeDocument document, CancellationToken ct)
    {
        var texts = Chunker.Split(document.Title, document.Body, Math.Max(200, _options().ChunkChars));
        if (texts.Count == 0)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmDocNoText));
        }
        IReadOnlyList<float[]> vectors;
        try
        {
            vectors = await _embeddings.EmbedAsync(texts, ct);
        }
        catch (LlmUnavailableException ex)
        {
            return AccessResult.Fail(AccessError.Unavailable, MessageCatalog.Now(Msg.AdmEmbedUnreachableSaved, MessageCatalog.Args(("message", ex.Message))));
        }
        if (vectors.Count != texts.Count || vectors.Any(v => v.Length != KnowledgeLimits.EmbeddingDimensions))
        {
            return AccessResult.Fail(AccessError.Unavailable,
                MessageCatalog.Now(Msg.AdmEmbedDims, MessageCatalog.Args(("dims", KnowledgeLimits.EmbeddingDimensions.ToString()))));
        }
        var chunks = texts.Select((text, index) => new KnowledgeChunk(index, text, vectors[index])).ToList();
        await _store.SaveAsync(document, chunks, ct);
        return AccessResult.Success;
    }

    private async Task<AccessResult?> FindDuplicateAsync(Guid? selfId, string title, string body, CancellationToken ct)
    {
        var titleKey = SameText(title);
        var bodyKey = SameText(body);
        foreach (var other in await _store.ListAsync(ct))
        {
            if (other.Id == selfId)
            {
                continue;
            }
            if (SameText(other.Title) == titleKey)
            {
                return AccessResult.Fail(AccessError.Conflict, MessageCatalog.Now(Msg.AdmDocDuplicateTitle, MessageCatalog.Args(("title", other.Title))));
            }
            var full = await _store.GetAsync(other.Id, ct);
            if (full is not null && SameText(full.Body) == bodyKey)
            {
                return AccessResult.Fail(AccessError.Conflict, MessageCatalog.Now(Msg.AdmDocDuplicateBody, MessageCatalog.Args(("title", other.Title))));
            }
        }
        return null;
    }

    private static string SameText(string text)
    {
        var folded = NameNormalizer.Fold(text).ToCharArray();
        for (var i = 0; i < folded.Length; i++)
        {
            if (!char.IsLetterOrDigit(folded[i]))
            {
                folded[i] = ' ';
            }
        }
        return string.Join(" ", new string(folded).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static AccessResult? Validate(string? title, string? body)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmDocTitleMissing));
        }
        if (string.IsNullOrWhiteSpace(body))
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmDocTextEmpty));
        }
        if (title.Trim().Length > KnowledgeLimits.MaxTitleLength)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmTitleTooLong, MessageCatalog.Args(("max", KnowledgeLimits.MaxTitleLength.ToString()))));
        }
        if (body.Trim().Length > KnowledgeLimits.MaxBodyLength)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmTextTooLong, MessageCatalog.Args(("max", KnowledgeLimits.MaxBodyLength.ToString()))));
        }
        return null;
    }

    private Task AuditAsync(AccessPrincipal actor, string action, KnowledgeDocument document, CancellationToken ct)
        => _audit(new AuditEntry(_clock(), actor.Account, action, document.Title, "id: " + document.Id), ct);
}
