using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Domain.Knowledge.Entities;

namespace CrmNlQuery.Application.Knowledge.Services;

/// <summary>
/// Knowledge store used when the database cannot support it; every call reports that the knowledge base is unavailable.
/// </summary>
public sealed class UnavailableKnowledgeStore : IKnowledgeStore, IKnowledgeAvailability
{
    public UnavailableKnowledgeStore(string reason)
    {
        Reason = reason;
    }

    public bool IsAvailable => false;

    public string? Reason { get; }

    private InvalidOperationException Unavailable() => new InvalidOperationException("The knowledge base is not available: " + Reason);

    /// <summary>
    /// Lists knowledge records, reporting that the store is unavailable.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<KnowledgeDocument>>(new List<KnowledgeDocument>());

    /// <summary>
    /// Gets knowledge records, reporting that the store is unavailable.
    /// </summary>
    public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<KnowledgeDocument?>(null);

    /// <summary>
    /// Saves knowledge records, reporting that the store is unavailable.
    /// </summary>
    public Task SaveAsync(KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken ct = default)
        => throw Unavailable();

    /// <summary>
    /// Sets enabled, reporting that the store is unavailable.
    /// </summary>
    public Task SetEnabledAsync(Guid id, bool enabled, string actor, DateTimeOffset at, CancellationToken ct = default)
        => throw Unavailable();

    /// <summary>
    /// Deletes knowledge records, reporting that the store is unavailable.
    /// </summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => throw Unavailable();

    /// <summary>
    /// Searches by vector, reporting that the store is unavailable.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeSnippet>> SearchByVectorAsync(float[] embedding, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<KnowledgeSnippet>>(new List<KnowledgeSnippet>());

    /// <summary>
    /// Searches by text, reporting that the store is unavailable.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeSnippet>> SearchByTextAsync(string question, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<KnowledgeSnippet>>(new List<KnowledgeSnippet>());
}
