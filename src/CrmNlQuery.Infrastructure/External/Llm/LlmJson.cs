using System.Text.Json;

namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Provides common reads from JSON replies of the Anthropic, Ollama and vLLM model servers.
/// </summary>
internal static class LlmJson
{
    /// <summary>
    /// Reads an integer property, returning null if missing, not a number or out of range.
    /// </summary>
    public static int? ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
    }
}
