using CrmNlQuery.Domain.Knowledge.Entities;

namespace CrmNlQuery.Application.Knowledge.Abstractions;

/// <summary>
/// Storage and raw search for knowledge documents; rules live in the service and result fusion happens above this layer.
/// </summary>
public interface IKnowledgeStore
{
    /// <summary>
    /// Lists knowledge documents, most recently updated first.
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(CancellationToken ct = default);
    /// <summary>
    /// Returns the knowledge document with the given id, or null when it does not exist.
    /// </summary>
    Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// Adds or updates a document and replaces all its chunks with the new ones.
    /// </summary>
    Task SaveAsync(KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken ct = default);
    /// <summary>
    /// Enables or disables a knowledge document.
    /// </summary>
    Task SetEnabledAsync(Guid id, bool enabled, string actor, DateTimeOffset at, CancellationToken ct = default);
    /// <summary>
    /// Deletes a knowledge document and its chunks.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Searches by vector similarity over chunks of active documents only, closest first, with cosine similarity as the score.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSnippet>> SearchByVectorAsync(float[] embedding, int take, CancellationToken ct = default);
    /// <summary>
    /// Searches chunk text by words from the question; the score is only for ordering.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSnippet>> SearchByTextAsync(string question, int take, CancellationToken ct = default);
}
