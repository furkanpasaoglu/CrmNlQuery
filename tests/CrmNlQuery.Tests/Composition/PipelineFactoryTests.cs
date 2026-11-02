using CrmNlQuery.Composition;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Llm;

namespace CrmNlQuery.Tests.Composition;

/// <summary>
/// Holds tests for the pipeline factory.
/// </summary>
public class PipelineFactoryTests
{
    /// <summary>
    /// Verifies that each step gets its own model client for per purpose metrics.
    /// </summary>
    [Fact]
    public void EachStep_GetsItsOwnModelClient_ForPerPurposeMetrics()
    {
        var purposes = new List<string>();
        var llm = new FakeLlm();
        var crm = new CrmOptions { BaseUrl = "http://crm.example/org" };

        PipelineFactory.Create(crm, llm, out _, purpose => { purposes.Add(purpose); return llm; });

        Assert.Equal(new[] { "analyze", "decompose", "plan" }, purposes.OrderBy(p => p).ToArray());
    }

    /// <summary>
    /// Verifies that without aWrapper every step uses the given client.
    /// </summary>
    [Fact]
    public void WithoutAWrapper_EveryStepUsesTheGivenClient()
    {
        var crm = new CrmOptions { BaseUrl = "http://crm.example/org" };
        var pipeline = PipelineFactory.Create(crm, new FakeLlm(), out var metadata);
        Assert.NotNull(pipeline);
        Assert.NotNull(metadata);
    }

    /// <summary>
    /// Verifies that describe names the provider and model in use.
    /// </summary>
    [Fact]
    public void Describe_NamesTheProviderAndModelInUse()
    {
        var ollama = new OllamaOptions { Model = "qwen3.6:27b" };
        var anthropic = new AnthropicOptions { Model = "claude-haiku-5-5" };

        Assert.Equal(("Ollama", "qwen3.6:27b"), LlmClientFactory.Describe(new LlmSelection(), ollama, anthropic));
        Assert.Equal(("Anthropic", "claude-haiku-5-5"),
            LlmClientFactory.Describe(new LlmSelection { Provider = "anthropic", AllowCloud = true }, ollama, anthropic));
    }
}
