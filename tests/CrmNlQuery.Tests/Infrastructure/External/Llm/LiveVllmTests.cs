using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Live protocol tests against a real vLLM server, skipped when the server is down.
/// </summary>
[Trait("Category", "Live")]
public class LiveVllmTests
{
    private const string AnswerSchema = """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"],"additionalProperties":false}""";

    private static VllmOptions Options(bool? think = false)
    {
        var maxLength = int.TryParse(Environment.GetEnvironmentVariable("Vllm__MaxModelLen"), out var parsed) ? parsed : 8192;
        return new VllmOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("Vllm__BaseUrl") ?? "http://127.0.0.1:8000",
            Model = Environment.GetEnvironmentVariable("Vllm__Model") ?? "qwen-test",
            ApiKey = Environment.GetEnvironmentVariable("Vllm__ApiKey") ?? "",
            MaxModelLen = maxLength,
            Think = think,
        };
    }

    /// <summary>
    /// Verifies that the model returns schema shaped JSON and reports token counts.
    /// </summary>
    [Fact]
    public async Task The_model_returns_schema_shaped_json_and_reports_token_counts()
    {
        var result = await VllmClient.Create(Options()).CompleteAsync("Reply with JSON only.", "What is 2+3?", AnswerSchema);

        Assert.Equal(5, JsonDocument.Parse(result.Text).RootElement.GetProperty("answer").GetInt32());
        Assert.True(result.Usage!.InputTokens > 0);
        Assert.True(result.Usage.OutputTokens > 0);
    }

    /// <summary>
    /// Verifies that with thinking on the JSON is still clean and thinking tokens are counted.
    /// </summary>
    [Fact]
    public async Task With_thinking_on_the_json_is_still_clean_and_the_thinking_tokens_are_counted()
    {
        var off = await VllmClient.Create(Options(think: false)).CompleteAsync("Reply with JSON only.", "What is 2+3?", AnswerSchema);
        var on = await VllmClient.Create(Options(think: true)).CompleteAsync("Reply with JSON only.", "What is 2+3?", AnswerSchema);

        Assert.Equal(5, JsonDocument.Parse(on.Text).RootElement.GetProperty("answer").GetInt32());
        Assert.True(on.Usage!.OutputTokens > off.Usage!.OutputTokens, $"thinking should add output tokens (off {off.Usage.OutputTokens}, on {on.Usage.OutputTokens})");
    }

    /// <summary>
    /// Verifies that a prompt larger than the window fails loudly with the server's numbers.
    /// </summary>
    [Fact]
    public async Task A_prompt_larger_than_the_window_fails_loudly_with_the_servers_numbers()
    {
        var options = Options();
        var filler = string.Join(" ", Enumerable.Range(0, options.MaxModelLen + 2000).Select(i => "word"));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => VllmClient.Create(options).CompleteAsync("Reply with JSON only.", filler, AnswerSchema));

        Assert.Equal(PromptTooLongException.PromptCause, ex.Cause);
        Assert.Equal(options.MaxModelLen, ex.ContextTokens);
        Assert.True(ex.PromptTokens > options.MaxModelLen);
    }

    /// <summary>
    /// Verifies that a wrong API key is reported clearly.
    /// </summary>
    [Fact]
    public async Task A_wrong_api_key_is_reported_clearly()
    {
        var options = Options();
        options.ApiKey = "definitely-wrong";

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => VllmClient.Create(options).CompleteAsync("s", "u", AnswerSchema));

        Assert.Contains("Vllm:ApiKey", ex.Message);
    }
}
