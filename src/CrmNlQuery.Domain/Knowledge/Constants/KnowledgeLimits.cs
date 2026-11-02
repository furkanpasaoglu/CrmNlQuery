namespace CrmNlQuery.Domain.Knowledge.Constants;

/// <summary>
/// Size limits for knowledge documents and search.
/// </summary>
public static class KnowledgeLimits
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 20000;
    public const int EmbeddingDimensions = 1024;
}
