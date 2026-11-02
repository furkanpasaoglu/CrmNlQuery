using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Knowledge.Constants;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Fake embedding client that maps each word to a fixed position in a 1024-dimension vector.
/// </summary>
public sealed class FakeEmbeddings : IEmbeddingClient
{
    public int Calls { get; private set; }
    public bool Down { get; set; }
    public int Dimensions { get; set; } = KnowledgeLimits.EmbeddingDimensions;

    /// <summary>
    /// Returns fake vectors for a list of texts without calling a model.
    /// </summary>
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        Calls++;
        if (Down)
        {
            throw new LlmUnavailableException("Cannot reach Ollama.");
        }
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());
    }

    /// <summary>
    /// Builds a deterministic vector for one text from its words.
    /// </summary>
    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in text.Split(new[] { ' ', '\n', '\t', '.', ',', ':', ';', '?', '!', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = NameNormalizer.Fold(word);
            var hash = 17;
            foreach (var ch in key)
            {
                hash = unchecked(hash * 31 + ch);
            }
            vector[(hash & int.MaxValue) % Dimensions] += 1;
        }
        return vector;
    }
}
