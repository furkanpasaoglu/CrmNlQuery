using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Client for the Claude API, a time-limited lab-only exception for fictional data.
/// </summary>
public sealed class AnthropicClient : ILlmClient
{
    private const string Url = "https://api.anthropic.com/v1/messages";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicClient(HttpClient http, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Anthropic API key is missing. Set the environment variable Anthropic__ApiKey.");
        }
        _http = http;
        _apiKey = apiKey;
        _model = model;
    }

    /// <summary>
    /// Creates an Anthropic client for the given API key and model.
    /// </summary>
    public static AnthropicClient Create(string apiKey, string model)
    {
        return new AnthropicClient(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, apiKey, model);
    }

    /// <summary>
    /// Sends a prompt to Claude and returns JSON output constrained by a schema.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return (await CompleteAsync(system, user, jsonSchema, ct)).Text;
    }

    /// <summary>
    /// Sends a prompt to Claude and returns plain text.
    /// </summary>
    public async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        var schema = JsonNode.Parse(jsonSchema)!;
        ForbidExtraProperties(schema);

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_tokens"] = 4096,
            ["system"] = system,
            ["messages"] = new object[] { new { role = "user", content = user } },
            ["output_config"] = new { format = new { type = "json_schema", schema } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(body) };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException($"Cannot reach the Anthropic API. ({ex.Message})", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmUnavailableException("The Anthropic API did not answer in time.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var shortText = text.Length > 300 ? text.Substring(0, 300) : text;
                var hint = (int)response.StatusCode == 401 ? " Check the Anthropic__ApiKey environment variable." : "";
                throw new LlmUnavailableException($"The Anthropic API returned {(int)response.StatusCode}: {shortText}.{hint}");
            }
            return new LlmResult(ReadText(text), ReadUsage(text));
        }
    }

    private static void ForbidExtraProperties(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var isObjectSchema = obj["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var typeName) && typeName == "object";
            if (isObjectSchema && !obj.ContainsKey("additionalProperties"))
            {
                obj["additionalProperties"] = false;
            }
            foreach (var child in obj.Select(p => p.Value).ToList())
            {
                if (child is not null) { ForbidExtraProperties(child); }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null) { ForbidExtraProperties(child); }
            }
        }
    }

    private static LlmUsage? ReadUsage(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return new LlmUsage(LlmJson.ReadInt(usage, "input_tokens"), LlmJson.ReadInt(usage, "output_tokens"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadText(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text")
                {
                    return block.GetProperty("text").GetString() ?? "";
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LlmUnavailableException("The Anthropic API returned an unexpected response.", ex);
        }
        throw new LlmUnavailableException("The Anthropic API reply had no text answer.");
    }
}
