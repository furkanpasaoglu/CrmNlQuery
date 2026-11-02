namespace CrmNlQuery.Domain.Knowledge.Entities;

/// <summary>
/// A searchable piece of a document with its embedding vector.
/// </summary>
public sealed record KnowledgeChunk(int Index, string Text, float[] Embedding);
