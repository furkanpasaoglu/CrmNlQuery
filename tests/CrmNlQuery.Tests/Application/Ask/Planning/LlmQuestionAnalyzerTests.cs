using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests the question analyzer's JSON mapping and prompt layout.
/// </summary>
public class LlmQuestionAnalyzerTests
{
    private static readonly IReadOnlyList<EntitySummary> Entities = new List<EntitySummary>
    {
        new EntitySummary("product", "products", "Product"),
        new EntitySummary("contract", "contracts", "Contract"),
        new EntitySummary("contractdetail", "contractdetails", "Contract Line"),
        new EntitySummary("account", "accounts", "Account"),
    };

    private const string Good = """
    {"primaryEntity":"contractdetail","relatedEntities":["contract","ghost"],
     "mentions":[{"text":"Örnekova","entity":"account"}],"columns":[],"aggregate":false}
    """;

    /// <summary>
    /// Verifies that model JSON is mapped and unknown related entities are dropped.
    /// </summary>
    [Fact]
    public async Task Maps_ModelJson_AndDropsUnknownRelatedEntities()
    {
        var llm = new FakeLlm(Good);
        var result = await new LlmQuestionAnalyzer(llm).AnalyzeAsync("Örnekova contractlines", Entities);

        Assert.Equal("contractdetail", result.PrimaryEntity);
        Assert.Equal(new[] { "contract" }, result.RelatedEntities);
        Assert.Equal("Örnekova", result.Mentions[0].Text);
        Assert.Empty(result.Columns);
        Assert.False(result.Aggregate);
    }

    /// <summary>
    /// Verifies that the prompt says filter-only attributes are not columns.
    /// </summary>
    [Fact]
    public async Task Prompt_SaysThatFilterOnlyAttributesAreNotColumns()
    {
        var llm = new FakeLlm(Good);
        await new LlmQuestionAnalyzer(llm).AnalyzeAsync("x", Entities);
        var system = llm.Calls[0].System;
        Assert.Contains("only used to filter", system);
        Assert.Contains("are NOT columns", system);
    }

    /// <summary>
    /// Verifies that the prompt lists entities with labels in the system message and only the question in the user message.
    /// </summary>
    [Fact]
    public async Task Prompt_ListsEntitiesWithLabelsInTheSystemMessage_AndOnlyTheQuestionInTheUserMessage()
    {
        var llm = new FakeLlm(Good);
        await new LlmQuestionAnalyzer(llm).AnalyzeAsync("Bütün productları getir", Entities);
        Assert.Contains("contractdetail | Contract Line", llm.Calls[0].System);
        Assert.DoesNotContain("contractdetail | Contract Line", llm.Calls[0].User);
        Assert.Equal("Question: Bütün productları getir", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that the system message is the same for every question so its prefix can be cached.
    /// </summary>
    [Fact]
    public async Task TheSystemMessage_IsTheSameForEveryQuestion_SoItsPrefixCanBeReused()
    {
        var first = new FakeLlm(Good);
        var second = new FakeLlm(Good);
        await new LlmQuestionAnalyzer(first).AnalyzeAsync("first question", Entities);
        await new LlmQuestionAnalyzer(second).AnalyzeAsync("a completely different question", Entities);

        Assert.Equal(first.Calls[0].System, second.Calls[0].System);
        Assert.NotEqual(first.Calls[0].User, second.Calls[0].User);
    }

    /// <summary>
    /// Verifies that the entity list comes before the optional rules so a follow-up never breaks the shared prefix.
    /// </summary>
    [Fact]
    public async Task TheEntityListComesBeforeTheOptionalRules_SoTheSharedPrefixIsNeverBrokenByAFollowUp()
    {
        var plain = new FakeLlm(Good);
        var follow = new FakeLlm(Good);
        await new LlmQuestionAnalyzer(plain).AnalyzeAsync("q", Entities);
        await new LlmQuestionAnalyzer(follow).AnalyzeAsync("q", Entities, new List<string>(), new PreviousQuery("accounts in London", "account", "<fetch />"));

        var shared = plain.Calls[0].System.TrimEnd();
        Assert.StartsWith(shared, follow.Calls[0].System, StringComparison.Ordinal);
        Assert.True(follow.Calls[0].System.Length > shared.Length);
    }

    /// <summary>
    /// Verifies that invalid JSON once is retried.
    /// </summary>
    [Fact]
    public async Task InvalidJsonOnce_IsRetried()
    {
        var llm = new FakeLlm("not json", Good);
        var result = await new LlmQuestionAnalyzer(llm).AnalyzeAsync("q", Entities);
        Assert.Equal("contractdetail", result.PrimaryEntity);
        Assert.Equal(2, llm.Calls.Count);
    }

    /// <summary>
    /// Verifies that invalid JSON twice throws an LLM output exception.
    /// </summary>
    [Fact]
    public async Task InvalidJsonTwice_ThrowsLlmOutputException()
    {
        var llm = new FakeLlm("garbage", "{broken");
        await Assert.ThrowsAsync<LlmOutputException>(() => new LlmQuestionAnalyzer(llm).AnalyzeAsync("q", Entities));
    }

    /// <summary>
    /// Verifies that missing fields are treated as empty.
    /// </summary>
    [Fact]
    public async Task MissingFields_AreTreatedAsEmpty()
    {
        var llm = new FakeLlm("""{"primaryEntity":"product"}""");
        var result = await new LlmQuestionAnalyzer(llm).AnalyzeAsync("q", Entities);
        Assert.Equal("product", result.PrimaryEntity);
        Assert.Empty(result.RelatedEntities);
        Assert.Empty(result.Mentions);
        Assert.Empty(result.Columns);
    }
}
