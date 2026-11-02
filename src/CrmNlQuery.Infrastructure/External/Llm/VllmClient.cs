using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Calls an OpenAI-compatible vLLM server for model completions.
/// </summary>
public sealed class VllmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly VllmOptions _options;

    private const int OverflowMargin = 8;
    private static readonly Regex MaxLength = new Regex(@"maximum (?:context )?length(?: is|:)?\s*\(?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PromptTokens = new Regex(@"(\d+)\s+(?:input\s+)?(?:tokens?\s+)?in\s+the\s+messages|input length\s*\(?(\d+)|passed\s+(\d+)\s+input tokens|prompt contains at least\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public VllmClient(HttpClient http, VllmOptions options)
    {
        EnsureHostAllowed(options);
        _http = http;
        _options = options;
    }

    /// <summary>
    /// Checks that the vLLM base address is local unless remote hosts are explicitly allowed.
    /// </summary>
    internal static void EnsureHostAllowed(VllmOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Vllm:BaseUrl is not a valid absolute URL: '{options.BaseUrl}'.");
        }
        var trusted = uri.IsLoopback || (options.TrustedHosts ?? new string[0]).Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        if (!trusted && !options.AllowRemoteHost)
        {
            throw new InvalidOperationException(
                $"Vllm:BaseUrl points to a non-local host ({uri.Host}); questions would leave this machine. " +
                "List the host in Vllm:TrustedHosts (preferred) or set Vllm:AllowRemoteHost=true only if this server is inside your own network and approved for KVKK.");
        }
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException("Llm:Provider is Vllm but Vllm:Model is empty. Use the name given to the server with --served-model-name.");
        }
    }

    /// <summary>
    /// Creates a vLLM client from the given options after validating them.
    /// </summary>
    public static VllmClient Create(VllmOptions options)
    {
        return new VllmClient(new HttpClient { Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes) }, options);
    }

    /// <summary>
    /// Requests a JSON completion constrained by the given schema and returns its text.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return (await CompleteAsync(system, user, jsonSchema, ct)).Text;
    }

    /// <summary>
    /// Requests a completion and returns the text with token usage.
    /// </summary>
    public async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["temperature"] = 0,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            ["response_format"] = new
            {
                type = "json_schema",
                json_schema = new { name = "answer", strict = true, schema = JsonDocument.Parse(jsonSchema).RootElement },
            },
        };
        if (_options.Think is not null)
        {
            body["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = _options.Think.Value };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + "/v1/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException($"Cannot reach vLLM at {_options.BaseUrl}. Is it running? ({ex.Message})", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmUnavailableException("vLLM did not answer in time.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ReadFailure((int)response.StatusCode, text);
            }
            try
            {
                using var doc = JsonDocument.Parse(text);
                var choice = doc.RootElement.GetProperty("choices")[0];
                var content = StripThinking(choice.GetProperty("message").TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "");
                var input = (int?)null;
                var output = (int?)null;
                if (doc.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    input = LlmJson.ReadInt(usage, "prompt_tokens");
                    output = LlmJson.ReadInt(usage, "completion_tokens");
                }
                var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                EnsureNotCutOff(finish, input, output);
                return new LlmResult(content, input is null && output is null ? null : new LlmUsage(input, output));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                throw new LlmUnavailableException("vLLM returned an unexpected response.", ex);
            }
        }
    }

    /// <summary>
    /// Removes leading think-tag reasoning text from the content when the server runs without a reasoning parser.
    /// </summary>
    internal static string StripThinking(string content)
    {
        var end = content.IndexOf("</think>", StringComparison.Ordinal);
        return end >= 0 && content.TrimStart().StartsWith("<think>", StringComparison.Ordinal) ? content[(end + "</think>".Length)..].TrimStart() : content;
    }

    private Exception ReadFailure(int status, string text)
    {
        if (status is 401 or 403)
        {
            return new LlmUnavailableException($"vLLM rejected the request ({status}); check Vllm:ApiKey.");
        }
        var overflow = ReadContextOverflow(text);
        if (overflow is not null)
        {
            return overflow;
        }
        var shortText = text.Length > 300 ? text.Substring(0, 300) : text;
        return new LlmUnavailableException($"vLLM returned {status}: {shortText}");
    }

    /// <summary>
    /// Reads vLLM's prompt-too-long error, taking the server's maximum length and the prompt tokens from the message.
    /// </summary>
    internal PromptTooLongException? ReadContextOverflow(string text)
    {
        string message;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? ""
                : root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var em) && em.ValueKind == JsonValueKind.String ? em.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return null;
        }
        if (message.Length == 0 || !message.Contains("context length", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var limit = MaxLength.Match(message) is { Success: true } max ? int.Parse(max.Groups[1].Value) : _options.MaxModelLen;
        var promptMatch = PromptTokens.Match(message);
        var prompt = promptMatch.Success ? int.Parse(promptMatch.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value) : 0;
        return new PromptTooLongException(PromptTooLongException.PromptCause, prompt, 0, limit);
    }

    private void EnsureNotCutOff(string? finishReason, int? promptTokens, int? outputTokens)
    {
        var prompt = promptTokens ?? 0;
        var output = outputTokens ?? 0;
        var limit = _options.MaxModelLen;
        if (finishReason == "length" || (limit > 0 && prompt + output >= limit - OverflowMargin))
        {
            throw new PromptTooLongException(PromptTooLongException.GenerationCause, prompt, output, limit);
        }
    }
}
