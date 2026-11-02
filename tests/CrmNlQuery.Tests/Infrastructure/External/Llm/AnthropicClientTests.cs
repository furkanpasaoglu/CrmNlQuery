using System.Net;
using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Tests the Anthropic LLM client: request shape, headers, response parsing, usage and error mapping.
/// </summary>
public class AnthropicClientTests
{
    private const string Schema = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}""";

    private const string TextReply =
        """{"id":"msg_1","type":"message","role":"assistant","content":[{"type":"text","text":"{\"ok\":true}"}],"stop_reason":"end_turn"}""";

    private static (AnthropicClient Client, FakeHandler Handler, List<HttpRequestMessage> Requests) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null, string model = "claude-sonnet-5-5")
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHandler(r =>
        {
            requests.Add(r);
            return (respond ?? (_ => FakeHandler.Json(TextReply)))(r);
        });
        return (new AnthropicClient(new HttpClient(handler), "test-key", model), handler, requests);
    }

    /// <summary>
    /// Verifies that sends model JSON schema format system and user.
    /// </summary>
    [Fact]
    public async Task SendsModel_JsonSchemaFormat_SystemAndUser()
    {
        var (client, handler, _) = Create();
        await client.CompleteJsonAsync("sys prompt", "user text", Schema);

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Urls[0]);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal("claude-sonnet-5-5", root.GetProperty("model").GetString());
        Assert.Equal("sys prompt", root.GetProperty("system").GetString());
        Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user text", root.GetProperty("messages")[0].GetProperty("content").GetString());
        var format = root.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("object", format.GetProperty("schema").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("tool_choice", out _));
        Assert.True(root.GetProperty("max_tokens").GetInt32() > 0);
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    /// <summary>
    /// Verifies that every object in the schema gets additional properties false.
    /// </summary>
    [Fact]
    public async Task EveryObjectInTheSchema_GetsAdditionalPropertiesFalse()
    {
        var nested = """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"a":{"type":"string"}}}}}}""";
        var (client, handler, _) = Create();
        await client.CompleteJsonAsync("s", "u", nested);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var schema = body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var inner = schema.GetProperty("properties").GetProperty("items").GetProperty("items");
        Assert.False(inner.GetProperty("additionalProperties").GetBoolean());
    }

    /// <summary>
    /// Verifies that sends API key and version headers.
    /// </summary>
    [Fact]
    public async Task SendsApiKeyAndVersionHeaders()
    {
        var (client, _, requests) = Create();
        await client.CompleteJsonAsync("s", "u", Schema);
        Assert.Equal("test-key", requests[0].Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", requests[0].Headers.GetValues("anthropic-version").Single());
    }

    /// <summary>
    /// Verifies that returns the text block as raw JSON.
    /// </summary>
    [Fact]
    public async Task ReturnsTheTextBlockAsRawJson()
    {
        var (client, _, _) = Create();
        var json = await client.CompleteJsonAsync("s", "u", Schema);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    /// <summary>
    /// Verifies that complete async reports token usage.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ReportsTokenUsage()
    {
        var reply = """{"content":[{"type":"text","text":"{\"ok\":true}"}],"usage":{"input_tokens":812,"output_tokens":57}}""";
        var (client, _, _) = Create(_ => FakeHandler.Json(reply));
        var result = await client.CompleteAsync("s", "u", Schema);
        Assert.Equal("{\"ok\":true}", result.Text);
        Assert.Equal(812, result.Usage!.InputTokens);
        Assert.Equal(57, result.Usage.OutputTokens);
    }

    /// <summary>
    /// Verifies that complete async usage is unknown when the reply has none.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_UsageIsUnknown_WhenTheReplyHasNone()
    {
        var (client, _, _) = Create();
        var result = await client.CompleteAsync("s", "u", Schema);
        Assert.Null(result.Usage);
    }

    /// <summary>
    /// Verifies that text block after thinking block is found.
    /// </summary>
    [Fact]
    public async Task TextBlockAfterThinkingBlock_IsFound()
    {
        var reply = """{"content":[{"type":"thinking","thinking":""},{"type":"text","text":"{\"ok\":false}"}]}""";
        var (client, _, _) = Create(_ => FakeHandler.Json(reply));
        var json = await client.CompleteJsonAsync("s", "u", Schema);
        Assert.Contains("false", json);
    }

    /// <summary>
    /// Verifies that no text block becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task NoTextBlock_BecomesLlmUnavailable()
    {
        var (client, _, _) = Create(_ => FakeHandler.Json("""{"content":[{"type":"thinking","thinking":""}]}"""));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that non JSON body becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task NonJsonBody_BecomesLlmUnavailable()
    {
        var (client, _, _) = Create(_ => FakeHandler.Json("not json"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that unauthorized becomes LLM unavailable with key hint.
    /// </summary>
    [Fact]
    public async Task Unauthorized_BecomesLlmUnavailable_WithKeyHint()
    {
        var (client, _, _) = Create(_ => FakeHandler.Json("""{"type":"error","error":{"type":"authentication_error"}}""", HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
        Assert.Contains("401", ex.Message);
        Assert.Contains("Anthropic__ApiKey", ex.Message);
    }

    /// <summary>
    /// Verifies that connection failure becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task ConnectionFailure_BecomesLlmUnavailable()
    {
        var (client, _, _) = Create(_ => throw new HttpRequestException("refused"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that timeout becomes LLM unavailable.
    /// </summary>
    [Fact]
    public async Task Timeout_BecomesLlmUnavailable()
    {
        var (client, _, _) = Create(_ => throw new TaskCanceledException());
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
        Assert.Contains("in time", ex.Message);
    }

    /// <summary>
    /// Verifies that blank API key is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankApiKey_IsRejected(string key)
    {
        Assert.Throws<InvalidOperationException>(() => new AnthropicClient(new HttpClient(), key, "claude-sonnet-5-5"));
    }
}
