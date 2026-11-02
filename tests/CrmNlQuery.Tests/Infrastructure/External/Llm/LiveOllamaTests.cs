using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Holds live tests against a running Ollama server.
/// </summary>
[Trait("Category", "Live")]
public class LiveOllamaTests
{
    /// <summary>
    /// Verifies that model returns schema shaped JSON.
    /// </summary>
    [Fact]
    public async Task Model_ReturnsSchemaShapedJson()
    {
        var options = new OllamaOptions { Model = Environment.GetEnvironmentVariable("Ollama__Model") ?? "qwen3:14b" };
        var client = OllamaClient.Create(options);
        var schema = """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""";

        var json = await client.CompleteJsonAsync("Reply with JSON only.", "What is 2+3?", schema);

        Assert.Equal(5, JsonDocument.Parse(json).RootElement.GetProperty("answer").GetInt32());
    }

    private const string AnswerSchema = """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""";

    /// <summary>
    /// Verifies that aPrompt larger than the window fails loudly instead of being cut silently.
    /// </summary>
    [Fact]
    public async Task APromptLargerThanTheWindow_FailsLoudly_InsteadOfBeingCutSilently()
    {
        var options = new OllamaOptions { Model = Environment.GetEnvironmentVariable("Ollama__Model") ?? "qwen3:14b", NumCtx = 512, Think = false };
        var client = OllamaClient.Create(options);
        var filler = string.Join(" ", Enumerable.Range(0, 1500).Select(i => "word" + i));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteAsync("Reply with JSON only.", filler + " What is 2+3?", AnswerSchema));

        Assert.Equal(PromptTooLongException.PromptCause, ex.Cause);
        Console.WriteLine($"Ollama reported prompt {ex.PromptTokens}, output {ex.OutputTokens}, window {ex.ContextTokens}");
    }

    /// <summary>
    /// Verifies that aShort prompt with the default window still answers.
    /// </summary>
    [Fact]
    public async Task AShortPrompt_WithTheDefaultWindow_StillAnswers()
    {
        var options = new OllamaOptions { Model = Environment.GetEnvironmentVariable("Ollama__Model") ?? "qwen3:14b", Think = false };
        var client = OllamaClient.Create(options);

        var result = await client.CompleteAsync("Reply with JSON only.", "What is 2+3?", AnswerSchema);

        Assert.Equal(5, JsonDocument.Parse(result.Text).RootElement.GetProperty("answer").GetInt32());
        Assert.True(result.Usage!.InputTokens > 0);
    }

    /// <summary>
    /// Verifies that aShared prefix is not evaluated again by the second call.
    /// </summary>
    [Fact]
    public async Task ASharedPrefix_IsNotEvaluatedAgain_ByTheSecondCall()
    {
        var options = new OllamaOptions { Model = Environment.GetEnvironmentVariable("Ollama__Model") ?? "qwen3:14b", Think = false };
        var client = OllamaClient.Create(options);
        var salt = Guid.NewGuid().ToString("N").Substring(0, 8);
        var prefix = string.Join(" ", Enumerable.Range(0, 800).Select(i => "entity" + salt + i + " | Label " + i));
        var system = "Reply with JSON only. Reference list: " + prefix;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await client.CompleteAsync(system, "Question: what is 2+3?", AnswerSchema);
        var cold = watch.ElapsedMilliseconds;
        watch.Restart();
        await client.CompleteAsync(system, "Question: what is 4+5?", AnswerSchema);
        var warm = watch.ElapsedMilliseconds;

        Console.WriteLine($"cold call {cold} ms, call with the same prefix {warm} ms");
        Assert.True(cold > 1000, $"a cold 8k-token prompt should take over a second (took {cold} ms): is the GPU that fast, or is the prefix cached already?");
        Assert.True(warm < cold / 3, $"the second call should reuse the cached prefix (cold {cold} ms, warm {warm} ms)");
    }
}
