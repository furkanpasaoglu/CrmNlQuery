using CrmNlQuery.Application.Knowledge.Services;

namespace CrmNlQuery.Application.Knowledge.Models;

/// <summary>
/// Holds the Knowledge settings; Enabled connects knowledge to the question pipeline while document management and test search always work.
/// </summary>
public sealed class KnowledgeOptions
{
    public bool Enabled { get; set; }
    public string EmbeddingModel { get; set; } = "bge-m3";
    public int TopK { get; set; } = 5;
    public double MinSimilarity { get; set; } = 0.5;
    public int MaxChars { get; set; } = 4000;
    public int ChunkChars { get; set; } = Chunker.DefaultMaxChars;
    public int Candidates { get; set; } = 10;
}
