namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Holds the selected LLM provider and model names.
/// </summary>
public sealed class LlmSelection
{
    public string Provider { get; set; } = "Ollama";
    public bool AllowCloud { get; set; }
}
