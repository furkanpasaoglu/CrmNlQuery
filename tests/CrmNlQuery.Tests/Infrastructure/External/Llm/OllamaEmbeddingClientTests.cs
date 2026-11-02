using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Tests the Ollama embedding client's requests and error handling.
/// </summary>
public class OllamaEmbeddingClientTests
{
    private static OllamaEmbeddingClient Client(FakeHandler handler, string baseUrl = "http://localhost:11434")
        => new OllamaEmbeddingClient(new HttpClient(handler), new OllamaOptions { BaseUrl = baseUrl }, "bge-m3");

    /// <summary>
    /// Verifies that Embed sends the model and all texts and returns one vector per text.
    /// </summary>
    [Fact]
    public async Task Embed_SendsModelAndAllTexts_AndReturnsOneVectorPerText()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"embeddings":[[0.5,1.0],[2.0,-1.0]]}"""));

        var vectors = await Client(handler).EmbedAsync(new[] { "bir", "iki" });

        Assert.Equal(2, vectors.Count);
        Assert.Equal(new[] { 0.5f, 1.0f }, vectors[0]);
        Assert.Equal(new[] { 2.0f, -1.0f }, vectors[1]);
        Assert.Equal("http://localhost:11434/api/embed", handler.Urls[0]);
        Assert.Contains("\"model\":\"bge-m3\"", handler.Bodies[0]);
        Assert.Contains("\"input\":[\"bir\",\"iki\"]", handler.Bodies[0]);
    }

    /// <summary>
    /// Verifies that embedding nothing does not call Ollama.
    /// </summary>
    [Fact]
    public async Task Embed_OfNothing_DoesNotCallOllama()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{}"));

        Assert.Empty(await Client(handler).EmbedAsync(new string[0]));
        Assert.Empty(handler.Urls);
    }

    /// <summary>
    /// Verifies that when Ollama answers with an error the message says the model may need pulling.
    /// </summary>
    [Fact]
    public async Task Embed_WhenOllamaAnswersWithAnError_SaysTheModelMayNeedPulling()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"error":"model not found"}""", System.Net.HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Client(handler).EmbedAsync(new[] { "x" }));

        Assert.Contains("ollama pull bge-m3", ex.Message);
    }

    /// <summary>
    /// Verifies that Embed fails when the returned count does not match.
    /// </summary>
    [Fact]
    public async Task Embed_WhenTheCountDoesNotMatch_Fails()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"embeddings":[[1.0]]}"""));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Client(handler).EmbedAsync(new[] { "a", "b" }));
    }

    /// <summary>
    /// Verifies that Embed fails when the response body is not what is expected.
    /// </summary>
    [Fact]
    public async Task Embed_WhenTheBodyIsNotWhatWeExpect_Fails()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"nope":1}"""));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Client(handler).EmbedAsync(new[] { "a" }));
    }

    /// <summary>
    /// Verifies that a non local host is refused like for the language model.
    /// </summary>
    [Fact]
    public void ANonLocalHost_IsRefused_LikeForTheLanguageModel()
    {
        Assert.Throws<InvalidOperationException>(() => Client(new FakeHandler(_ => FakeHandler.Json("{}")), "http://ai.example.com:11434"));
    }
}
