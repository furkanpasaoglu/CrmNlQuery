using System.Net;
using System.Text.Json;
using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Checks the model server typed in the setup wizard: Ollama lists its models, vLLM lists its served models, Anthropic answers a models request; redirects are not followed and no response body is returned.
/// </summary>
public sealed class LlmSetupProbe : IModelProbe
{
    /// <summary>
    /// Asks the server for its model list within 10 seconds and checks that the chosen model is among them.
    /// </summary>
    public async Task<IReadOnlyList<SetupCheck>> RunAsync(ModelProbeInput input, CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var anthropic = input.Provider == "Anthropic";
        var url = anthropic ? "https://api.anthropic.com/v1/models" : input.BaseUrl.Trim().TrimEnd('/') + (input.Provider == "Vllm" ? "/v1/models" : "/api/tags");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (anthropic)
        {
            request.Headers.Add("x-api-key", input.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (!string.IsNullOrWhiteSpace(input.ApiKey))
        {
            request.Headers.Add("Authorization", "Bearer " + input.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            return One("model.reach", SetupCheckStatus.Fail, "unreachable");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return One("model.reach", SetupCheckStatus.Fail, "timeout");
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return One("model.reach", SetupCheckStatus.Fail, "unauthorized");
            }
            if (!response.IsSuccessStatusCode)
            {
                return One("model.reach", SetupCheckStatus.Fail, "error");
            }
            var checks = new List<SetupCheck> { new SetupCheck("model.reach", SetupCheckStatus.Ok) };
            var names = await ReadNamesAsync(response, input.Provider, ct);
            if (names is null)
            {
                checks.Add(new SetupCheck("model.list", SetupCheckStatus.Warn, "unexpected_answer"));
                return checks;
            }
            var present = names.Any(n => string.Equals(n, input.Model, StringComparison.OrdinalIgnoreCase)
                || (input.Provider == "Ollama" && !input.Model.Contains(':') && n.StartsWith(input.Model + ":", StringComparison.OrdinalIgnoreCase)));
            checks.Add(new SetupCheck("model.present", present ? SetupCheckStatus.Ok : SetupCheckStatus.Fail, present ? null : "not_listed"));
            return checks;
        }
    }

    private static async Task<List<string>?> ReadNamesAsync(HttpResponseMessage response, string provider, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var list = new List<string>();
            var array = provider == "Ollama" ? doc.RootElement.GetProperty("models") : doc.RootElement.GetProperty("data");
            var key = provider == "Ollama" ? "name" : "id";
            foreach (var item in array.EnumerateArray())
            {
                if (item.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.String) { list.Add(n.GetString()!); }
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<SetupCheck> One(string id, SetupCheckStatus status, string detail)
    {
        return new List<SetupCheck> { new SetupCheck(id, status, detail) };
    }
}
