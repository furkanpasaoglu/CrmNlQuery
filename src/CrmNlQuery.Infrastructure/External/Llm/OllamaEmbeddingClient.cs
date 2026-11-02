using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Turns text into embedding vectors with the local Ollama embedding model, independent of the chat model.
/// </summary>
public sealed class OllamaEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly string _model;

    public OllamaEmbeddingClient(HttpClient http, OllamaOptions options, string model)
    {
        OllamaClient.EnsureHostAllowed(options);
        _http = http;
        _options = options;
        _model = model;
    }

    /// <summary>
    /// Creates an embedding client for the given Ollama options and model.
    /// </summary>
    public static OllamaEmbeddingClient Create(OllamaOptions options, string model)
    {
        return new OllamaEmbeddingClient(new HttpClient { Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes) }, options, model);
    }

    /// <summary>
    /// Returns an embedding vector for each text from the local Ollama server.
    /// </summary>
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return new List<float[]>();
        }

        var url = _options.BaseUrl.TrimEnd('/') + "/api/embed";
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, new { model = _model, input = texts }, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException($"Cannot reach Ollama at {_options.BaseUrl}. Is it running? ({ex.Message})", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmUnavailableException("Ollama did not answer in time.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var shortText = text.Length > 300 ? text.Substring(0, 300) : text;
                throw new LlmUnavailableException($"Ollama returned {(int)response.StatusCode} for the embedding model '{_model}' (is it pulled? ollama pull {_model}): {shortText}");
            }
            try
            {
                using var doc = JsonDocument.Parse(text);
                var vectors = new List<float[]>();
                foreach (var row in doc.RootElement.GetProperty("embeddings").EnumerateArray())
                {
                    vectors.Add(row.EnumerateArray().Select(v => v.GetSingle()).ToArray());
                }
                if (vectors.Count != texts.Count)
                {
                    throw new LlmUnavailableException($"Ollama returned {vectors.Count} embeddings for {texts.Count} texts.");
                }
                return vectors;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new LlmUnavailableException("Ollama returned an unexpected embedding response.", ex);
            }
        }
    }
}
