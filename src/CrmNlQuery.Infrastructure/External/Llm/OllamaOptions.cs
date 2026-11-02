namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Settings for the Ollama local model client such as host, model, context size and thinking.
/// </summary>
public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.6:27b";
    public bool? Think { get; set; }
    public bool? ThinkAnalyze { get; set; }
    public bool? ThinkDecompose { get; set; }
    public bool? ThinkPlan { get; set; }
    public bool? ThinkCreate { get; set; }
    public string? KeepAlive { get; set; }

    /// <summary>
    /// Returns a copy of the options for one step (analyze, decompose or plan), using the step-specific thinking setting if set, else the general one.
    /// </summary>
    public OllamaOptions ForStep(string purpose)
    {
        var copy = (OllamaOptions)MemberwiseClone();
        var step = purpose switch { "analyze" => ThinkAnalyze, "decompose" => ThinkDecompose, "plan" => ThinkPlan, "create" => ThinkCreate ?? false, _ => null };
        copy.Think = step ?? Think;
        return copy;
    }
    public int NumCtx { get; set; } = 16384;
    public int TimeoutMinutes { get; set; } = 10;
    public bool AllowRemoteHost { get; set; }
    public string[] TrustedHosts { get; set; } = new string[0];
}
