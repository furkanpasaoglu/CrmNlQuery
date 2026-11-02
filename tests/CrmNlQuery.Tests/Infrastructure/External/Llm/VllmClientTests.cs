using System.Net;
using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Tests the vLLM client for the OpenAI-compatible chat API.
/// </summary>
public class VllmClientTests
{
    private const string Schema = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}""";
    private const string Reply = """{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":120,"completion_tokens":7}}""";

    private static VllmOptions Options(Action<VllmOptions>? change = null)
    {
        var options = new VllmOptions { Model = "qwen-test", MaxModelLen = 16384 };
        change?.Invoke(options);
        return options;
    }

    private static (VllmClient Client, FakeHandler Handler, List<HttpRequestMessage> Requests) Create(VllmOptions options, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHandler(request => { requests.Add(request); return (respond ?? (_ => FakeHandler.Json(Reply)))(request); });
        return (new VllmClient(new HttpClient(handler), options), handler, requests);
    }

    /// <summary>
    /// Verifies that a local base URL is accepted.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:8000")]
    [InlineData("http://127.0.0.1:8000/")]
    [InlineData("http://[::1]:8000")]
    public void LocalBaseUrl_IsAccepted(string baseUrl)
    {
        Create(Options(o => o.BaseUrl = baseUrl));
    }

    /// <summary>
    /// Verifies that a remote base URL is rejected unless explicitly allowed.
    /// </summary>
    [Theory]
    [InlineData("http://models.example.com:8000")]
    [InlineData("https://gpu.example.com")]
    [InlineData("http://localhost.evil.com:8000")]
    public void RemoteBaseUrl_IsRejected_UnlessAllowed(string baseUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(Options(o => o.BaseUrl = baseUrl)));
        Assert.Contains("AllowRemoteHost", ex.Message);

        Create(Options(o => { o.BaseUrl = baseUrl; o.AllowRemoteHost = true; }));
    }

    /// <summary>
    /// Verifies that a host listed as trusted is accepted while every other remote host stays rejected.
    /// </summary>
    [Fact]
    public void TrustedHost_IsAccepted_ButOtherRemoteHostsAreNot()
    {
        Create(Options(o => { o.BaseUrl = "http://vllm:8000"; o.TrustedHosts = new[] { "vllm" }; }));

        Assert.Throws<InvalidOperationException>(() => Create(Options(o => { o.BaseUrl = "http://evil.example.com:8000"; o.TrustedHosts = new[] { "vllm" }; })));
    }

    /// <summary>
    /// Verifies that a missing model name is reported at once.
    /// </summary>
    [Fact]
    public void A_missing_model_name_is_reported_at_once()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(Options(o => o.Model = " ")));

        Assert.Contains("Vllm:Model", ex.Message);
    }

    /// <summary>
    /// Verifies that an OpenAI chat request carries the schema and that text and usage are read from the reply.
    /// </summary>
    [Fact]
    public async Task Sends_an_openai_chat_request_with_the_schema_and_reads_text_and_usage()
    {
        var (client, handler, _) = Create(Options());

        var result = await client.CompleteAsync("system text", "user text", Schema);

        Assert.Equal("""{"ok":true}""", result.Text);
        Assert.Equal(120, result.Usage!.InputTokens);
        Assert.Equal(7, result.Usage.OutputTokens);
        Assert.Equal("http://localhost:8000/v1/chat/completions", handler.Urls.Single());
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        Assert.Equal("qwen-test", root.GetProperty("model").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user text", root.GetProperty("messages")[1].GetProperty("content").GetString());
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("boolean", format.GetProperty("json_schema").GetProperty("schema").GetProperty("properties").GetProperty("ok").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("chat_template_kwargs", out _));
    }

    /// <summary>
    /// Verifies that the thinking setting is passed to the chat template.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_thinking_setting_goes_to_the_chat_template(bool think)
    {
        var (client, handler, _) = Create(Options(o => o.Think = think));

        await client.CompleteJsonAsync("s", "u", Schema);

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal(think, body.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    /// <summary>
    /// Verifies that each step gets its own thinking setting and the create step defaults to off.
    /// </summary>
    [Fact]
    public void Each_step_gets_its_own_thinking_setting_and_the_create_step_defaults_to_off()
    {
        var options = Options(o => { o.Think = true; o.ThinkAnalyze = false; });

        Assert.False(options.ForStep("analyze").Think);
        Assert.True(options.ForStep("plan").Think);
        Assert.False(options.ForStep("create").Think);
        Assert.True(options.Think);
    }

    /// <summary>
    /// Verifies that the API key is sent as a bearer token and only when set.
    /// </summary>
    [Fact]
    public async Task The_api_key_is_sent_as_a_bearer_token_and_only_when_set()
    {
        var (withKey, _, withKeyRequests) = Create(Options(o => o.ApiKey = "secret-key"));
        var (withoutKey, _, withoutKeyRequests) = Create(Options());

        await withKey.CompleteJsonAsync("s", "u", Schema);
        await withoutKey.CompleteJsonAsync("s", "u", Schema);

        Assert.Equal("Bearer secret-key", withKeyRequests.Single().Headers.Authorization!.ToString());
        Assert.Null(withoutKeyRequests.Single().Headers.Authorization);
    }

    /// <summary>
    /// Verifies that a rejected API key gives a clear error without echoing the key.
    /// </summary>
    [Fact]
    public async Task A_rejected_api_key_gives_a_clear_error_without_echoing_the_key()
    {
        var (client, _, _) = Create(Options(o => o.ApiKey = "secret-key"), _ => FakeHandler.Json("""{"error":"Unauthorized"}""", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));

        Assert.Contains("Vllm:ApiKey", ex.Message);
        Assert.DoesNotContain("secret-key", ex.Message);
    }

    /// <summary>
    /// Verifies that an unreachable server is reported as unavailable.
    /// </summary>
    [Fact]
    public async Task A_server_that_cannot_be_reached_is_reported_as_unavailable()
    {
        var client = new VllmClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("connection refused"))), Options());

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));

        Assert.Contains("Cannot reach vLLM", ex.Message);
    }

    /// <summary>
    /// Verifies that a reply cut off by the length limit is a generation overflow.
    /// </summary>
    [Fact]
    public async Task A_reply_cut_off_by_the_length_limit_is_a_generation_overflow()
    {
        const string cut = """{"choices":[{"index":0,"finish_reason":"length","message":{"role":"assistant","content":"{\"ok\":"}}],"usage":{"prompt_tokens":3000,"completion_tokens":13384}}""";
        var (client, _, _) = Create(Options(), _ => FakeHandler.Json(cut));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteJsonAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.GenerationCause, ex.Cause);
        Assert.Equal(16384, ex.ContextTokens);
    }

    /// <summary>
    /// Verifies that the real vllm overflow message is turned into a prompt overflow.
    /// </summary>
    [Fact]
    public async Task The_real_vllm_overflow_message_is_turned_into_a_prompt_overflow()
    {
        const string real = """{"error":{"message":"This model's maximum context length is 8192 tokens. However, you requested 0 output tokens and your prompt contains at least 8193 input tokens, for a total of at least 8193 tokens. Please reduce the length of the input prompt or the number of requested output tokens. (parameter=input_tokens, value=8193)","type":"BadRequestError","param":"input_tokens","code":400}}""";
        var (client, _, _) = Create(Options(), _ => FakeHandler.Json(real, HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteJsonAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.PromptCause, ex.Cause);
        Assert.Equal(8193, ex.PromptTokens);
        Assert.Equal(8192, ex.ContextTokens);
    }

    /// <summary>
    /// Verifies that the older vLLM overflow message wording is understood too.
    /// </summary>
    [Fact]
    public async Task The_older_vllm_overflow_message_is_understood_too()
    {
        const string older = """{"object":"error","message":"This model's maximum context length is 4096 tokens. However, you requested 5000 tokens (4800 in the messages, 200 in the completion). Please reduce the length of the messages or completion.","type":"BadRequestError","param":null,"code":400}""";
        var (client, _, _) = Create(Options(), _ => FakeHandler.Json(older, HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteJsonAsync("s", "u", Schema));

        Assert.Equal(4800, ex.PromptTokens);
        Assert.Equal(4096, ex.ContextTokens);
    }

    /// <summary>
    /// Verifies that a real length cut with null content is a generation overflow.
    /// </summary>
    [Fact]
    public async Task A_real_length_cut_with_null_content_is_a_generation_overflow()
    {
        const string real = """{"choices":[{"index":0,"message":{"role":"assistant","content":null,"reasoning":"Okay, the user"},"finish_reason":"length"}],"usage":{"prompt_tokens":8159,"total_tokens":8192,"completion_tokens":33}}""";
        var (client, _, _) = Create(Options(), _ => FakeHandler.Json(real));

        var ex = await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteJsonAsync("s", "u", Schema));

        Assert.Equal(PromptTooLongException.GenerationCause, ex.Cause);
        Assert.Equal(8159, ex.PromptTokens);
    }

    /// <summary>
    /// Verifies that thinking text in front of the JSON is dropped.
    /// </summary>
    [Fact]
    public async Task Thinking_text_in_front_of_the_json_is_dropped()
    {
        const string withThink = """{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"<think>\nlet me see\n</think>\n{\"ok\":true}"}}],"usage":{"prompt_tokens":10,"completion_tokens":20}}""";
        var (client, _, _) = Create(Options(), _ => FakeHandler.Json(withThink));

        Assert.Equal("""{"ok":true}""", await client.CompleteJsonAsync("s", "u", Schema));
    }

    /// <summary>
    /// Verifies that an unexpected reply is reported as unavailable.
    /// </summary>
    [Fact]
    public async Task An_unexpected_reply_is_reported_as_unavailable()
    {
        var (client, _, _) = Create(Options(), _ => FakeHandler.Json("""{"nothing":true}"""));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", Schema));
    }
}
