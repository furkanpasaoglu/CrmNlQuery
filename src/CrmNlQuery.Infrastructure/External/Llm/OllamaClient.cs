using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Client for the local Ollama model, sending JSON-structured and plain completion requests.
/// </summary>
public sealed class OllamaClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    private const int OverflowMargin = 8;

    public OllamaClient(HttpClient http, OllamaOptions options)
    {
        EnsureHostAllowed(options);
        _http = http;
        _options = options;
    }

    /// <summary>
    /// Checks that the Ollama host is local or explicitly allowed, rejecting remote hosts otherwise.
    /// </summary>
    internal static void EnsureHostAllowed(OllamaOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Ollama:BaseUrl is not a valid absolute URL: '{options.BaseUrl}'.");
        }

        var isLocal = uri.IsLoopback || (options.TrustedHosts ?? new string[0]).Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        if (!isLocal && !options.AllowRemoteHost)
        {
            throw new InvalidOperationException(
                $"Ollama:BaseUrl points to a non-local host ({uri.Host}); questions would leave this machine. " +
                "List the host in Ollama:TrustedHosts (preferred) or set Ollama:AllowRemoteHost=true only if this server is inside your own network and approved for KVKK.");
        }
    }

    /// <summary>
    /// Creates an Ollama client from the given options.
    /// </summary>
    public static OllamaClient Create(OllamaOptions options)
    {
        return new OllamaClient(new HttpClient { Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes) }, options);
    }

    /// <summary>
    /// Sends a prompt to Ollama and returns JSON output constrained by a schema.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return (await CompleteAsync(system, user, jsonSchema, ct)).Text;
    }

    /// <summary>
    /// Sends a prompt to Ollama and returns plain text.
    /// </summary>
    public async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            ["format"] = JsonDocument.Parse(jsonSchema).RootElement,
            ["options"] = Options(),
        };
        if (_options.Think is not null)
        {
            body["think"] = _options.Think;
        }
        if (!string.IsNullOrWhiteSpace(_options.KeepAlive))
        {
            body["keep_alive"] = int.TryParse(_options.KeepAlive, out var seconds) ? seconds : _options.KeepAlive;
        }
        if (_options.NumCtx > 0)
        {
            body["truncate"] = false;
        }

        var url = _options.BaseUrl.TrimEnd('/') + "/api/chat";
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, body, ct);
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
                var overflow = ReadContextOverflow(text);
                if (overflow is not null)
                {
                    throw overflow;
                }
                var shortText = text.Length > 300 ? text.Substring(0, 300) : text;
                throw new LlmUnavailableException($"Ollama returned {(int)response.StatusCode}: {shortText}");
            }
            try
            {
                using var doc = JsonDocument.Parse(text);
                var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
                var input = LlmJson.ReadInt(doc.RootElement, "prompt_eval_count");
                var output = LlmJson.ReadInt(doc.RootElement, "eval_count");
                EnsureWindowNotFull(input, output, doc.RootElement);
                return new LlmResult(content, input is null && output is null ? null : new LlmUsage(input, output));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new LlmUnavailableException("Ollama returned an unexpected response.", ex);
            }
        }
    }

    private static PromptTooLongException? ReadContextOverflow(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }
            if (error.ValueKind == JsonValueKind.String)
            {
                var inner = error.GetString() ?? "";
                return inner.Contains("exceed_context_size_error", StringComparison.Ordinal) ? ReadContextOverflow(inner) : null;
            }
            if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("type", out var type) || type.GetString() != "exceed_context_size_error")
            {
                return null;
            }
            return new PromptTooLongException(PromptTooLongException.PromptCause, LlmJson.ReadInt(error, "n_prompt_tokens") ?? 0, 0, LlmJson.ReadInt(error, "n_ctx") ?? 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Dictionary<string, object> Options()
    {
        var options = new Dictionary<string, object> { ["temperature"] = 0 };
        if (_options.NumCtx > 0)
        {
            options["num_ctx"] = _options.NumCtx;
        }
        return options;
    }

    private void EnsureWindowNotFull(int? promptTokens, int? outputTokens, JsonElement root)
    {
        if (_options.NumCtx <= 0)
        {
            return;
        }
        var cutByLength = root.TryGetProperty("done_reason", out var reason) && reason.ValueKind == JsonValueKind.String && reason.GetString() == "length";
        if (promptTokens is null && outputTokens is null && !cutByLength)
        {
            return;
        }
        var prompt = promptTokens ?? 0;
        var output = outputTokens ?? 0;
        var limit = _options.NumCtx - OverflowMargin;
        if (prompt >= limit)
        {
            throw new PromptTooLongException(PromptTooLongException.PromptCause, prompt, output, _options.NumCtx);
        }
        if (cutByLength || prompt + output >= limit)
        {
            throw new PromptTooLongException(PromptTooLongException.GenerationCause, prompt, output, _options.NumCtx);
        }
    }
}
