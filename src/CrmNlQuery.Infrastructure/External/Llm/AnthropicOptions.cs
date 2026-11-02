namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Holds Anthropic API options such as the key and model.
/// </summary>
public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-haiku-5-5";
}
