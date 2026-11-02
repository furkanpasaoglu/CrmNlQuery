namespace CrmNlQuery.Infrastructure.External.Llm;

/// <summary>
/// Holds the vLLM server settings used when the provider is set to vLLM.
/// </summary>
public sealed class VllmOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool? Think { get; set; }
    public bool? ThinkAnalyze { get; set; }
    public bool? ThinkDecompose { get; set; }
    public bool? ThinkPlan { get; set; }
    public bool? ThinkCreate { get; set; }
    public int MaxModelLen { get; set; }
    public int TimeoutMinutes { get; set; } = 10;
    public bool AllowRemoteHost { get; set; }
    public string[] TrustedHosts { get; set; } = new string[0];

    /// <summary>
    /// Returns a copy of the settings for one step (analyze, decompose, plan, create or design) with its own thinking setting if present.
    /// </summary>
    public VllmOptions ForStep(string purpose)
    {
        var copy = (VllmOptions)MemberwiseClone();
        var step = purpose switch { "analyze" => ThinkAnalyze, "decompose" => ThinkDecompose, "plan" => ThinkPlan, "create" => ThinkCreate ?? false, _ => null };
        copy.Think = step ?? Think;
        return copy;
    }
}
