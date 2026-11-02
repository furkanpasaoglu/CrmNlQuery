namespace CrmNlQuery.Domain.Knowledge.Entities;

/// <summary>
/// A chunk found by search with its combined relevance score and source.
/// </summary>
public sealed record KnowledgeSnippet(Guid DocumentId, int ChunkIndex, string Title, string Text, double Score, string Source);
