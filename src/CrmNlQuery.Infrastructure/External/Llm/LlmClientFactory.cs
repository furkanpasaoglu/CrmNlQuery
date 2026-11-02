using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Creates LLM clients for the selected provider, with explicit errors for unknown providers.
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// Checks whether the selection uses a cloud provider.
    /// </summary>
    public static bool IsCloud(LlmSelection selection)
    {
        return Normalize(selection.Provider) == "anthropic";
    }

    /// <summary>
    /// Checks whether the selection uses vLLM.
    /// </summary>
    public static bool IsVllm(LlmSelection selection)
    {
        return Normalize(selection.Provider) == "vllm";
    }

    /// <summary>
    /// Returns the provider and model name used for metrics records.
    /// </summary>
    public static (string Provider, string Model) Describe(LlmSelection selection, OllamaOptions ollama, AnthropicOptions anthropic, VllmOptions? vllm = null)
    {
        if (IsCloud(selection))
        {
            return ("Anthropic", anthropic.Model);
        }
        return IsVllm(selection) ? ("vLLM", (vllm ?? new VllmOptions()).Model) : ("Ollama", ollama.Model);
    }

    /// <summary>
    /// Creates a local-provider client for one step, applying that step's own thinking setting.
    /// </summary>
    public static ILlmClient CreateForStep(LlmSelection selection, OllamaOptions ollama, VllmOptions vllm, string purpose)
    {
        return IsVllm(selection) ? VllmClient.Create(vllm.ForStep(purpose)) : OllamaClient.Create(ollama.ForStep(purpose));
    }

    /// <summary>
    /// Creates the LLM client for the selection, failing explicitly instead of falling back to another model for an unknown provider.
    /// </summary>
    public static ILlmClient Create(LlmSelection selection, OllamaOptions ollama, AnthropicOptions anthropic, VllmOptions? vllm = null)
    {
        switch (Normalize(selection.Provider))
        {
            case "ollama":
                return OllamaClient.Create(ollama);
            case "vllm":
                return VllmClient.Create(vllm ?? new VllmOptions());
            case "anthropic":
                if (!selection.AllowCloud)
                {
                    throw new InvalidOperationException(
                        "Llm:Provider is Anthropic (cloud) but Llm:AllowCloud is not true. Questions would leave this machine. " +
                        "Set Llm:AllowCloud=true only for lab work with fake data.");
                }
                if (string.IsNullOrWhiteSpace(anthropic.ApiKey))
                {
                    throw new InvalidOperationException("Llm:Provider is Anthropic but Anthropic:ApiKey is empty.");
                }
                return AnthropicClient.Create(anthropic.ApiKey, anthropic.Model);
            default:
                throw new InvalidOperationException($"Unknown Llm:Provider '{selection.Provider}'. Use Ollama, Vllm or Anthropic.");
        }
    }

    private static string Normalize(string? provider)
    {
        var value = (provider ?? "").Trim().ToLowerInvariant();
        return value.Length == 0 ? "ollama" : value;
    }
}
