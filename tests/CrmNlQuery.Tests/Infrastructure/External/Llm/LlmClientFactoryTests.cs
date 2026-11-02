using CrmNlQuery.Infrastructure.External.Llm;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Unit tests for the LLM client factory choosing the right provider and per-step client from configuration.
/// </summary>
public class LlmClientFactoryTests
{
    private static LlmSelection Pick(string provider, bool allowCloud = false) => new LlmSelection { Provider = provider, AllowCloud = allowCloud };

    /// <summary>
    /// Verifies that default is ollama not the cloud.
    /// </summary>
    [Fact]
    public void Default_IsOllama_NotTheCloud()
    {
        var client = LlmClientFactory.Create(new LlmSelection(), new OllamaOptions(), new AnthropicOptions());
        Assert.IsType<OllamaClient>(client);
        Assert.False(LlmClientFactory.IsCloud(new LlmSelection()));
    }

    /// <summary>
    /// Verifies that ollama is case insensitive.
    /// </summary>
    [Theory]
    [InlineData("ollama")]
    [InlineData("OLLAMA")]
    [InlineData("  Ollama ")]
    public void Ollama_IsCaseInsensitive(string provider)
    {
        Assert.IsType<OllamaClient>(LlmClientFactory.Create(Pick(provider), new OllamaOptions(), new AnthropicOptions()));
    }

    /// <summary>
    /// Verifies that anthropic without allow cloud is refused even with a key.
    /// </summary>
    [Fact]
    public void Anthropic_WithoutAllowCloud_IsRefused_EvenWithAKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Pick("Anthropic", allowCloud: false), new OllamaOptions(), new AnthropicOptions { ApiKey = "sk-test" }));
        Assert.Contains("AllowCloud", ex.Message);
    }

    /// <summary>
    /// Verifies that anthropic without a key is refused with a helpful message.
    /// </summary>
    [Fact]
    public void Anthropic_WithoutAKey_IsRefused_WithAHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Pick("Anthropic", allowCloud: true), new OllamaOptions(), new AnthropicOptions { ApiKey = "  " }));
        Assert.Contains("Anthropic:ApiKey", ex.Message);
    }

    /// <summary>
    /// Verifies that anthropic with permission and key is created and counts as cloud.
    /// </summary>
    [Fact]
    public void Anthropic_WithPermissionAndKey_IsCreated_AndCountsAsCloud()
    {
        var selection = Pick("anthropic", allowCloud: true);
        var client = LlmClientFactory.Create(selection, new OllamaOptions(), new AnthropicOptions { ApiKey = "sk-test" });
        Assert.IsType<AnthropicClient>(client);
        Assert.True(LlmClientFactory.IsCloud(selection));
    }

    /// <summary>
    /// Verifies that vllm is case insensitive needs no cloud permission and is not cloud.
    /// </summary>
    [Theory]
    [InlineData("vllm")]
    [InlineData("VLLM")]
    [InlineData("  Vllm ")]
    public void Vllm_IsCaseInsensitive_NeedsNoCloudPermission_AndIsNotCloud(string provider)
    {
        var selection = Pick(provider);

        var client = LlmClientFactory.Create(selection, new OllamaOptions(), new AnthropicOptions(), new VllmOptions { Model = "qwen-test" });

        Assert.IsType<VllmClient>(client);
        Assert.False(LlmClientFactory.IsCloud(selection));
        Assert.True(LlmClientFactory.IsVllm(selection));
    }

    /// <summary>
    /// Verifies that vllm describe names the provider and the served model.
    /// </summary>
    [Fact]
    public void Vllm_Describe_NamesTheProviderAndTheServedModel()
    {
        var described = LlmClientFactory.Describe(Pick("Vllm"), new OllamaOptions(), new AnthropicOptions(), new VllmOptions { Model = "qwen-test" });

        Assert.Equal(("vLLM", "qwen-test"), described);
    }

    /// <summary>
    /// Verifies that vllm without a model name is refused with a helpful message.
    /// </summary>
    [Fact]
    public void Vllm_WithoutAModelName_IsRefused_WithAHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Pick("Vllm"), new OllamaOptions(), new AnthropicOptions(), new VllmOptions()));

        Assert.Contains("Vllm:Model", ex.Message);
    }

    /// <summary>
    /// Verifies that for step picks the client of the selected provider.
    /// </summary>
    [Fact]
    public void ForStep_PicksTheClientOfTheSelectedProvider()
    {
        var vllm = new VllmOptions { Model = "qwen-test" };

        Assert.IsType<VllmClient>(LlmClientFactory.CreateForStep(Pick("Vllm"), new OllamaOptions(), vllm, "plan"));
        Assert.IsType<OllamaClient>(LlmClientFactory.CreateForStep(Pick("Ollama"), new OllamaOptions(), vllm, "plan"));
    }

    /// <summary>
    /// Verifies that unknown provider is refused never silently falling back.
    /// </summary>
    [Fact]
    public void UnknownProvider_IsRefused_NeverSilentlyFallingBack()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Pick("gemini"), new OllamaOptions(), new AnthropicOptions()));
        Assert.Contains("Llm:Provider", ex.Message);
    }

    /// <summary>
    /// Verifies that anthropic defaults are haiku and no key.
    /// </summary>
    [Fact]
    public void AnthropicDefaults_AreHaiku_AndNoKey()
    {
        var options = new AnthropicOptions();
        Assert.Equal("claude-haiku-5-5", options.Model);
        Assert.Equal("", options.ApiKey);
    }
}
