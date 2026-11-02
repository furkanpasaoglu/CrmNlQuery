using CrmNlQuery.Composition;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Tests.Composition;

/// <summary>
/// Tests the eval composition's Ollama options and context window handling.
/// </summary>
public class EvalCompositionTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    /// <summary>
    /// Verifies that the eval ollama options keep the configured context window.
    /// </summary>
    [Fact]
    public void TheEvalOllamaOptions_KeepTheConfiguredContextWindow()
    {
        var options = EvalComposition.OllamaOptionsFor(Config(("Ollama:NumCtx", "32768"), ("Ollama:BaseUrl", "http://localhost:11434")), "m1", true);

        Assert.Equal(32768, options.NumCtx);
        Assert.Equal("m1", options.Model);
        Assert.True(options.Think);
    }

    /// <summary>
    /// Verifies that without a configured value the window is the default 16384.
    /// </summary>
    [Fact]
    public void WithoutAConfiguredValue_TheWindowIsTheDefault_16384()
    {
        Assert.Equal(16384, EvalComposition.OllamaOptionsFor(Config(), "m1", null).NumCtx);
        Assert.Equal(16384, EvalComposition.ContextWindow(Config()));
        Assert.Equal(0, EvalComposition.ContextWindow(Config(("Ollama:NumCtx", "0"))));
    }

    /// <summary>
    /// Verifies that the eval Ollama options carry the per-step think settings and keep-alive.
    /// </summary>
    [Fact]
    public void TheEvalOllamaOptions_CarryTheStepThinkSettingsAndKeepAlive()
    {
        var options = EvalComposition.OllamaOptionsFor(Config(("Ollama:ThinkPlan", "true"), ("Ollama:ThinkAnalyze", "false"), ("Ollama:KeepAlive", "30m")), "m1", null);

        Assert.Equal("30m", options.KeepAlive);
        Assert.True(options.ForStep("plan").Think);
        Assert.False(options.ForStep("analyze").Think);
        Assert.Null(options.ForStep("decompose").Think);
    }

    /// <summary>
    /// Verifies that a think value given on the command line wins for every step.
    /// </summary>
    [Fact]
    public void ThinkGivenOnTheCommandLine_WinsForEveryStep()
    {
        var options = EvalComposition.OllamaOptionsFor(Config(("Ollama:ThinkPlan", "true"), ("Ollama:ThinkAnalyze", "false")), "m1", false);

        Assert.False(options.ForStep("plan").Think);
        Assert.False(options.ForStep("analyze").Think);
        Assert.False(options.ForStep("decompose").Think);
    }
}
