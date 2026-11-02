namespace CrmNlQuery.Application.Knowledge.Abstractions;

/// <summary>
/// Converts text to a meaning vector, implemented locally so document text never leaves the machine.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// Converts a list of texts into embedding vectors, one float array per input, in the same order.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
