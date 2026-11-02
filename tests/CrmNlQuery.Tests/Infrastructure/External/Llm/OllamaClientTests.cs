using System.Net;
using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Holds tests for the Ollama client: URL rules, request options, token usage, context overflow and error mapping.
/// </summary>
public class OllamaClientTests
{
    private const string Schema = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}""";
    private const string Reply = """{"message":{"role":"assistant","content":"{\"ok\":true}"}}""";

    private static (OllamaClient Client, FakeHandler Handler) Create(OllamaOptions options, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new FakeHandler(respond ?? (_ => FakeHandler.Json(Reply)));
        return (new OllamaClient(new HttpClient(handler), options), handler);
    }

    /// <summary>
    /// Verifies that local base URL is accepted.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://LOCALHOST:11434/")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://[::1]:11434")]
    public void LocalBaseUrl_IsAccepted(string baseUrl)
    {
        Create(new OllamaOptions { BaseUrl = baseUrl });
    }

    /// <summary>
    /// Verifies that remote base URL is rejected unless allowed.
    /// </summary>
    [Theory]
    [InlineData("http://models.example.com:11434")]
    [InlineData("https://ollama.example.com")]
    [InlineData("http://localhost.evil.com:11434")]
    public void RemoteBaseUrl_IsRejected_UnlessAllowed(string baseUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(new OllamaOptions { BaseUrl = baseUrl }));
        Assert.Contains("AllowRemoteHost", ex.Message);

        Create(new OllamaOptions { BaseUrl = baseUrl, AllowRemoteHost = true });
    }

    /// <summary>
    /// Verifies that a host listed as trusted is accepted while every other remote host stays rejected.
    /// </summary>
    [Fact]
    public void TrustedHost_IsAccepted_ButOtherRemoteHostsAreNot()
    {
        Create(new OllamaOptions { BaseUrl = "http://host.docker.internal:11434", TrustedHosts = new[] { "HOST.docker.internal" } });

        Assert.Throws<InvalidOperationException>(() => Create(new OllamaOptions { BaseUrl = "http://evil.example.com:11434", TrustedHosts = new[] { "host.docker.internal" } }));
    }

    /// <summary>
    /// Verifies that invalid base URL is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    public void InvalidBaseUrl_IsRejected(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => Create(new OllamaOptions { BaseUrl = baseUrl, AllowRemoteHost = true }));
    }

    /// <summary>
    /// Verifies that sends model schema no streaming and zero temperature.
    /// </summary>
    [Fact]
    public async Task Sends_Model_Schema_NoStreaming_AndZeroTemperature()
    {
        var (client, handler) = Create(new OllamaOptions { Model = "qwen3:14b" });
        var content = await client.CompleteJsonAsync("sys", "usr", Schema);

        Assert.Equal("{\"ok\":true}", content);
        Assert.EndsWith("/api/chat", handler.Urls[0]);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal("qwen3:14b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("format").ValueKind);
        Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetInt32());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("usr", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("think", out _));
    }

    /// <summary>
    /// Verifies that complete async reports token usage when ollama gives it.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ReportsTokenUsage_WhenOllamaGivesIt()
    {
        var reply = """{"message":{"role":"assistant","content":"{\"ok\":true}"},"prompt_eval_count":120,"eval_count":34}""";
        var (client, _) = Create(new OllamaOptions(), _ => FakeHandler.Json(reply));
        var result = await client.CompleteAsync("s", "u", Schema);
        Assert.Equal("{\"ok\":true}", result.Text);
        Assert.Equal(120, result.Usage!.InputTokens);
        Assert.Equal(34, result.Usage.OutputTokens);
    }

    /// <summary>
    /// Verifies that complete async usage is unknown when ollama does not report it.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_UsageIsUnknown_WhenOllamaDoesNotReportIt()
    {
        var (client, _) = Create(new OllamaOptions());
        var result = await client.CompleteAsync("s", "u", Schema);
        Assert.Equal("{\"ok\":true}", result.Text);
        Assert.Null(result.Usage);
    }

    /// <summary>
    /// Verifies that num ctx defaults to16384 and is sent in the options.
    /// </summary>
    [Fact]
    public async Task NumCtx_DefaultsTo16384_AndIsSentInTheOptions()
    {
        var (client, handler) = Create(new OllamaOptions());
        await client.CompleteJsonAsync("s", "u", Schema);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(16384, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    /// <summary>
    /// Verifies that with aWindow truncation is turned off so ollama refuses instead of cutting silently.
    /// </summary>
    [Fact]
    public async Task WithAWindow_TruncationIsTurnedOff_SoOllamaRefusesInsteadOfCuttingSilently()
    {
        var (client, handler) = Create(new OllamaOptions { NumCtx = 4096 });
        await client.CompleteJsonAsync("s", "u", Schema);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());

        var (off, offHandler) = Create(new OllamaOptions { NumCtx = 0 });
        await off.CompleteJsonAsync("s", "u", Schema);
        using var offBody = JsonDocument.Parse(offHandler.Bodies[0]);
        Assert.False(offBody.RootElement.TryGetProperty("truncate", out _));
    }

    private const string ExceedBody = "{\"error\":{\"code\":400,\"message\":\"request (6423 tokens) exceeds the available context size (512 tokens), try increasing it\",\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":6423,\"n_ctx\":512}}";

    /// <summary>
    /// Verifies that ollamas exceed context error becomes aPrompt overflow with the real token counts.
    /// </summary>
    [Fact]
    public async Task OllamasExceedContextError_BecomesAPromptOverflow_WithTheRealTokenCounts()
    {
        var (client, _) = Create(new OllamaOptions { NumCtx = 512 }, _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(ExceedBody) });

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.PromptCause, ex.Cause);
        Assert.Equal(6423, ex.PromptTokens);
        Assert.Equal(512, ex.ContextTokens);
    }

    /// <summary>
    /// Verifies that other bad requests stay ordinary model errors.
    /// </summary>
    [Fact]
    public async Task OtherBadRequests_StayOrdinaryModelErrors()
    {
        var (client, _) = Create(new OllamaOptions { NumCtx = 512 }, _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"model not found\"}") });

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that keep alive is not sent by default aSent as text or number when configured.
    /// </summary>
    [Fact]
    public async Task KeepAlive_IsNotSentByDefault_ASentAsTextOrNumberWhenConfigured()
    {
        var (plain, plainHandler) = Create(new OllamaOptions());
        await plain.CompleteJsonAsync("s", "u", Schema);
        using (var body = JsonDocument.Parse(plainHandler.Bodies[0])) { Assert.False(body.RootElement.TryGetProperty("keep_alive", out _)); }

        var (text, textHandler) = Create(new OllamaOptions { KeepAlive = "30m" });
        await text.CompleteJsonAsync("s", "u", Schema);
        using (var body = JsonDocument.Parse(textHandler.Bodies[0])) { Assert.Equal("30m", body.RootElement.GetProperty("keep_alive").GetString()); }

        var (number, numberHandler) = Create(new OllamaOptions { KeepAlive = "-1" });
        await number.CompleteJsonAsync("s", "u", Schema);
        using (var body = JsonDocument.Parse(numberHandler.Bodies[0])) { Assert.Equal(-1, body.RootElement.GetProperty("keep_alive").GetInt32()); }
    }

    /// <summary>
    /// Verifies that for step uses the steps think setting else the global one and never changes the original.
    /// </summary>
    [Fact]
    public void ForStep_UsesTheStepsThinkSetting_ElseTheGlobalOne_AndNeverChangesTheOriginal()
    {
        var options = new OllamaOptions { Think = true, ThinkAnalyze = false, ThinkPlan = null, Model = "m", NumCtx = 8192, KeepAlive = "10m" };

        var analyze = options.ForStep("analyze");
        var plan = options.ForStep("plan");
        var decompose = options.ForStep("decompose");
        var unknown = options.ForStep("something-else");

        Assert.False(analyze.Think);
        Assert.True(plan.Think);
        Assert.True(decompose.Think);
        Assert.True(unknown.Think);
        Assert.Equal("m", analyze.Model);
        Assert.Equal(8192, analyze.NumCtx);
        Assert.Equal("10m", analyze.KeepAlive);
        Assert.True(options.Think);
        Assert.NotSame(options, analyze);
    }

    /// <summary>
    /// Verifies that for step with nothing configured keeps think unset so no think setting is sent.
    /// </summary>
    [Fact]
    public void ForStep_WithNothingConfigured_KeepsThinkUnset_SoNoThinkSettingIsSent()
    {
        var options = new OllamaOptions();

        Assert.Null(options.ForStep("analyze").Think);
        Assert.Null(options.ForStep("plan").Think);
    }

    /// <summary>
    /// Verifies that num ctx is not sent when set to zero.
    /// </summary>
    [Fact]
    public async Task NumCtx_IsNotSent_WhenSetToZero()
    {
        var (client, handler) = Create(new OllamaOptions { NumCtx = 0 });
        await client.CompleteJsonAsync("s", "u", Schema);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.False(body.RootElement.GetProperty("options").TryGetProperty("num_ctx", out _));
    }

    private static string ReplyWith(int promptTokens, int outputTokens, string? doneReason = null)
    {
        var done = doneReason is null ? "" : ",\"done_reason\":\"" + doneReason + "\"";
        return "{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"ok\\\":true}\"},\"prompt_eval_count\":" + promptTokens + ",\"eval_count\":" + outputTokens + done + "}";
    }

    /// <summary>
    /// Verifies that when the prompt alone fills the window it fails with cause prompt.
    /// </summary>
    [Fact]
    public async Task WhenThePromptAloneFillsTheWindow_ItFailsWithCausePrompt()
    {
        var (client, _) = Create(new OllamaOptions { NumCtx = 1000 }, _ => FakeHandler.Json(ReplyWith(995, 5)));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.PromptCause, ex.Cause);
        Assert.Equal(995, ex.PromptTokens);
        Assert.Equal(5, ex.OutputTokens);
        Assert.Equal(1000, ex.ContextTokens);
    }

    /// <summary>
    /// Verifies that when the prompt fits but thinking and output fill the window it fails with cause generation.
    /// </summary>
    [Fact]
    public async Task WhenThePromptFitsButThinkingAndOutputFillTheWindow_ItFailsWithCauseGeneration()
    {
        var (client, _) = Create(new OllamaOptions { NumCtx = 1000 }, _ => FakeHandler.Json(ReplyWith(400, 595)));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.GenerationCause, ex.Cause);
        Assert.Equal(400, ex.PromptTokens);
        Assert.Equal(595, ex.OutputTokens);
    }

    /// <summary>
    /// Verifies that done reason length is aGeneration overflow even below the margin.
    /// </summary>
    [Fact]
    public async Task DoneReasonLength_IsAGenerationOverflow_EvenBelowTheMargin()
    {
        var (client, _) = Create(new OllamaOptions { NumCtx = 1000 }, _ => FakeHandler.Json(ReplyWith(200, 100, "length")));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.GenerationCause, ex.Cause);
    }

    /// <summary>
    /// Verifies that just below the margin still succeeds.
    /// </summary>
    [Fact]
    public async Task JustBelowTheMargin_StillSucceeds()
    {
        var (client, _) = Create(new OllamaOptions { NumCtx = 1000 }, _ => FakeHandler.Json(ReplyWith(500, 491)));

        var result = await client.CompleteAsync("s", "u", Schema);

        Assert.Equal("{\"ok\":true}", result.Text);
    }

    /// <summary>
    /// Verifies that no overflow decision when ollama reports no counts or num ctx is off.
    /// </summary>
    [Fact]
    public async Task NoOverflowDecision_WhenOllamaReportsNoCounts_OrNumCtxIsOff()
    {
        var (noCounts, _) = Create(new OllamaOptions { NumCtx = 1000 });
        Assert.Equal("{\"ok\":true}", (await noCounts.CompleteAsync("s", "u", Schema)).Text);

        var (off, _) = Create(new OllamaOptions { NumCtx = 0 }, _ => FakeHandler.Json(ReplyWith(5000, 5000)));
        Assert.Equal("{\"ok\":true}", (await off.CompleteAsync("s", "u", Schema)).Text);
    }

    /// <summary>
    /// Verifies that think setting is sent when configured.
    /// </summary>
    [Fact]
    public async Task ThinkSetting_IsSentWhenConfigured()
    {
        var (client, handler) = Create(new OllamaOptions { Think = false });
        await client.CompleteJsonAsync("s", "u", Schema);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.False(body.RootElement.GetProperty("think").GetBoolean());
    }

    /// <summary>
    /// Verifies that HTTP error becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task HttpError_BecomesLlmUnavailable()
    {
        var (client, _) = Create(new OllamaOptions(), _ => FakeHandler.Json("""{"error":"model not found"}""", HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
        Assert.Contains("404", ex.Message);
    }

    /// <summary>
    /// Verifies that non JSON body becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task NonJsonBody_BecomesLlmUnavailable()
    {
        var (client, _) = Create(new OllamaOptions(), _ => FakeHandler.Json("this is not json"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that missing message field becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task MissingMessageField_BecomesLlmUnavailable()
    {
        var (client, _) = Create(new OllamaOptions(), _ => FakeHandler.Json("{}"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that timeout becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task Timeout_BecomesLlmUnavailable()
    {
        var (client, _) = Create(new OllamaOptions(), _ => throw new TaskCanceledException());
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
        Assert.Contains("in time", ex.Message);
    }

    /// <summary>
    /// Verifies that connection failure becomes LLM unavailable with helpful message.
    /// </summary>
    [Fact]
    public async Task ConnectionFailure_BecomesLlmUnavailable_WithHelpfulMessage()
    {
        var (client, _) = Create(new OllamaOptions(), _ => throw new HttpRequestException("connection refused"));
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
        Assert.Contains("Ollama", ex.Message);
    }
}
