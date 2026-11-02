using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Unit tests for the LLM plan generator: prompt building, retries, validation feedback and failure handling with a fake model.
/// </summary>
public class LlmPlanGeneratorTests
{
    private static readonly string GoodPlan =
        "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"productid\\\" /><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";

    private static readonly string BadPlan =
        "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"productname\\\" /></entity></fetch>\"}]}";

    private static PlanRequest Request() => new PlanRequest(
        "all products",
        new List<EntityInfo> { TestMetadata.Product() },
        new List<ResolvedRecord>(),
        new List<string> { "productid", "name" },
        false);

    private static LlmPlanGenerator Generator(FakeLlm llm) => new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create()));

    /// <summary>
    /// Verifies that valid first attempt succeeds with one model call.
    /// </summary>
    [Fact]
    public async Task ValidFirstAttempt_Succeeds_WithOneModelCall()
    {
        var llm = new FakeLlm(GoodPlan);
        var outcome = await Generator(llm).GenerateAsync(Request());
        Assert.True(outcome.Success);
        Assert.Contains("top=\"500\"", outcome.FetchXml);
        Assert.Single(llm.Calls);
    }

    /// <summary>
    /// Verifies that invalid then valid retries and feeds errors back to model.
    /// </summary>
    [Fact]
    public async Task InvalidThenValid_RetriesAndFeedsErrorsBackToModel()
    {
        var llm = new FakeLlm(BadPlan, GoodPlan);
        var outcome = await Generator(llm).GenerateAsync(Request());
        Assert.True(outcome.Success);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("productname", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that garbage three times fails without throwing.
    /// </summary>
    [Fact]
    public async Task GarbageThreeTimes_FailsWithoutThrowing()
    {
        var llm = new FakeLlm("garbage", "{", "nope");
        var outcome = await Generator(llm).GenerateAsync(Request());
        Assert.False(outcome.Success);
        Assert.Equal(LlmPlanGenerator.MaxAttempts, llm.Calls.Count);
        Assert.NotEmpty(outcome.Errors);
    }

    /// <summary>
    /// Verifies that invalid three times returns last validation errors.
    /// </summary>
    [Fact]
    public async Task InvalidThreeTimes_ReturnsLastValidationErrors()
    {
        var llm = new FakeLlm(BadPlan, BadPlan, BadPlan);
        var outcome = await Generator(llm).GenerateAsync(Request());
        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("productname"));
    }

    /// <summary>
    /// Verifies that prompt contains schema resolved records and columns but no row data.
    /// </summary>
    [Fact]
    public async Task Prompt_ContainsSchemaResolvedRecordsAndColumns_ButNoRowData()
    {
        var llm = new FakeLlm(GoodPlan);
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new PlanRequest("contracts of Örnekova",
            new List<EntityInfo> { TestMetadata.Contract() },
            new List<ResolvedRecord> { new ResolvedRecord("Örnekova", "account", id, "Örnekova Holding A.Ş.") },
            new List<string> { "title" }, false);
        await Generator(llm).GenerateAsync(request);

        var user = llm.Calls[0].User;
        Assert.Contains("contract", user);
        Assert.Contains("customerid", user);
        Assert.Contains(id.ToString(), user);
        Assert.Contains("Örnekova Holding A.Ş.", user);
        Assert.Contains("title", user);
    }

    /// <summary>
    /// Verifies that the user message starts with the entity blocks and ends with the question and columns.
    /// </summary>
    [Fact]
    public async Task TheUserMessage_StartsWithTheEntityBlocks_AndEndsWithTheQuestionAndColumns()
    {
        var llm = new FakeLlm(GoodPlan);
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new PlanRequest("contracts of Örnekova",
            new List<EntityInfo> { TestMetadata.Contract() },
            new List<ResolvedRecord> { new ResolvedRecord("Örnekova", "account", id, "Örnekova Holding A.Ş.") },
            new List<string> { "title" }, false);
        await Generator(llm).GenerateAsync(request);

        var user = llm.Calls[0].User;
        Assert.StartsWith("Entity contract", user);
        Assert.True(user.IndexOf("Entity contract", StringComparison.Ordinal) < user.IndexOf("Resolved records", StringComparison.Ordinal));
        Assert.True(user.IndexOf("Resolved records", StringComparison.Ordinal) < user.IndexOf("Question: contracts of", StringComparison.Ordinal));
        Assert.True(user.IndexOf("Question: contracts of", StringComparison.Ordinal) < user.IndexOf("Columns: title", StringComparison.Ordinal));
        Assert.EndsWith("Columns: title", user.TrimEnd());
    }

    /// <summary>
    /// Verifies that the entity blocks are sorted by name so the same set gives the same prefix whatever the order.
    /// </summary>
    [Fact]
    public async Task TheEntityBlocks_AreSortedByName_SoTheSameSetGivesTheSamePrefix_WhateverTheOrder()
    {
        var one = new FakeLlm(GoodPlan);
        var two = new FakeLlm(GoodPlan);
        PlanRequest Make(params EntityInfo[] entities) => new PlanRequest("q", entities.ToList(), new List<ResolvedRecord>(), new List<string> { "name" }, false);

        await Generator(one).GenerateAsync(Make(TestMetadata.Product(), TestMetadata.Account(), TestMetadata.Contract()));
        await Generator(two).GenerateAsync(Make(TestMetadata.Contract(), TestMetadata.Product(), TestMetadata.Account()));

        string Blocks(string user) => user.Substring(0, user.IndexOf("Root entity", StringComparison.Ordinal));
        Assert.Equal(Blocks(one.Calls[0].User), Blocks(two.Calls[0].User));
        var user = one.Calls[0].User;
        Assert.True(user.IndexOf("Entity account", StringComparison.Ordinal) < user.IndexOf("Entity contract", StringComparison.Ordinal));
        Assert.True(user.IndexOf("Entity contract", StringComparison.Ordinal) < user.IndexOf("Entity product", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that the previous attempts errors stay at the very end.
    /// </summary>
    [Fact]
    public async Task ThePreviousAttemptsErrors_StayAtTheVeryEnd()
    {
        var llm = new FakeLlm(BadPlan, GoodPlan);
        await Generator(llm).GenerateAsync(Request());

        var retry = llm.Calls[1].User;
        Assert.True(retry.IndexOf("Columns:", StringComparison.Ordinal) < retry.IndexOf("Your previous attempt failed validation", StringComparison.Ordinal));
        Assert.True(retry.IndexOf("Entity product", StringComparison.Ordinal) < retry.IndexOf("Question:", StringComparison.Ordinal));
    }

    private static EntityInfo ProductWithNoise() => TestMetadata.Product() with
    {
        Attributes = TestMetadata.Product().Attributes.Concat(new[]
        {
            new AttributeInfo("yominame", "Yomi name", "String", new List<OptionInfo>()),
            new AttributeInfo("statecodename", "State name", "Virtual", new List<OptionInfo>()),
        }).ToList(),
    };

    private static PlanRequest NoisyRequest() => new PlanRequest("all products", new List<EntityInfo> { ProductWithNoise() }, new List<ResolvedRecord>(),
        new List<string> { "productid", "name" }, false);

    /// <summary>
    /// Verifies that the first attempt gets the pruned entity blocks.
    /// </summary>
    [Fact]
    public async Task TheFirstAttempt_GetsThePrunedEntityBlocks()
    {
        var llm = new FakeLlm(GoodPlan);
        await Generator(llm).GenerateAsync(NoisyRequest());

        Assert.DoesNotContain("yominame", llm.Calls[0].User);
        Assert.DoesNotContain("statecodename", llm.Calls[0].User);
        Assert.Contains("productnumber", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that after a failure the retry gets the full unpruned list.
    /// </summary>
    [Fact]
    public async Task AfterAFailure_TheRetryGetsTheFullUnprunedList()
    {
        var llm = new FakeLlm(BadPlan, GoodPlan);
        var outcome = await Generator(llm).GenerateAsync(NoisyRequest());

        Assert.True(outcome.Success);
        Assert.DoesNotContain("yominame", llm.Calls[0].User);
        Assert.Contains("yominame", llm.Calls[1].User);
        Assert.Contains("statecodename", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that with pruning switched off every attempt gets the full list.
    /// </summary>
    [Fact]
    public async Task WithPruningSwitchedOff_EveryAttemptGetsTheFullList()
    {
        var llm = new FakeLlm(GoodPlan);
        await new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create()), pruneContext: false).GenerateAsync(NoisyRequest());

        Assert.Contains("yominame", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that the system prompt tells the model to exclude empty values when ranking by smallest or earliest.
    /// </summary>
    [Fact]
    public async Task TheSystemPrompt_TellsTheModelToExcludeEmptyValues_WhenRankingBySmallestOrEarliest()
    {
        var llm = new FakeLlm(GoodPlan);
        await Generator(llm).GenerateAsync(Request());

        var system = llm.Calls[0].System;
        Assert.Contains("smallest, fewest, lowest or earliest", system);
        Assert.Contains("operator=\"not-null\"", system);
        Assert.Contains("CRM sorts empty values FIRST", system);
    }

    /// <summary>
    /// Verifies that the multi select note appears only when an entity has a multi select attribute.
    /// </summary>
    [Fact]
    public async Task TheMultiSelectNote_AppearsOnlyWhenAnEntityHasAMultiSelectAttribute()
    {
        var withMulti = ProductWithNoise() with
        {
            Attributes = ProductWithNoise().Attributes.Concat(new[] { new AttributeInfo("new_features", "Features", "MultiSelectPicklist", new List<OptionInfo> { new OptionInfo(100000000, "Sunroof") }) }).ToList(),
        };
        var plain = new FakeLlm(GoodPlan);
        var multi = new FakeLlm(GoodPlan);
        await Generator(plain).GenerateAsync(NoisyRequest());
        await Generator(multi).GenerateAsync(NoisyRequest() with { Entities = new List<EntityInfo> { withMulti } });

        Assert.DoesNotContain("contain-values", plain.Calls[0].User);
        Assert.Contains("contain-values", multi.Calls[0].User);
        Assert.Contains("100000000=Sunroof", multi.Calls[0].User);
    }

    private static readonly string AccountPlan =
        "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"account\\\"><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";

    /// <summary>
    /// Verifies that wrong root entity is rejected and retried with clear error.
    /// </summary>
    [Fact]
    public async Task WrongRootEntity_IsRejected_AndRetriedWithClearError()
    {
        var llm = new FakeLlm(AccountPlan, GoodPlan);
        var outcome = await Generator(llm).GenerateAsync(Request());
        Assert.True(outcome.Success);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("must be 'product'", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that wrong root entity three times fails.
    /// </summary>
    [Fact]
    public async Task WrongRootEntityThreeTimes_Fails()
    {
        var llm = new FakeLlm(AccountPlan, AccountPlan, AccountPlan);
        var outcome = await Generator(llm).GenerateAsync(Request());
        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("must be 'product'"));
    }

    /// <summary>
    /// Verifies that LLM unavailable propagates.
    /// </summary>
    [Fact]
    public async Task LlmUnavailable_Propagates()
    {
        var generator = new LlmPlanGenerator(new ThrowingLlm(), new PlanValidator(TestMetadata.Create()));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => generator.GenerateAsync(Request()));
    }

    /// <summary>
    /// Verifies that the examples are generated from the requests entities and live in the user message not in the system message.
    /// </summary>
    [Fact]
    public async Task TheExamples_AreGeneratedFromTheRequestsEntities_AndLiveInTheUserMessage_NotInTheSystemMessage()
    {
        var llm = new FakeLlm(GoodPlan);
        await Generator(llm).GenerateAsync(Request());

        var call = llm.Calls[0];
        Assert.DoesNotContain("Examples of valid FetchXML", call.System);
        Assert.DoesNotContain("contractdetail", call.System);
        Assert.Contains("Root entity (the entity whose rows the user wants to see): product", call.User);
        Assert.Contains("Examples of valid FetchXML for these entities:", call.User);
        Assert.Contains("<entity name=\"product\">", call.User);
        Assert.True(call.User.IndexOf("Entity product", StringComparison.Ordinal) < call.User.IndexOf("Examples of valid FetchXML", StringComparison.Ordinal));
        Assert.True(call.User.IndexOf("Examples of valid FetchXML", StringComparison.Ordinal) < call.User.IndexOf("Question:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that the system message is the same for every request so its prefix is always shared.
    /// </summary>
    [Fact]
    public async Task TheSystemMessage_IsTheSameForEveryRequest_SoItsPrefixIsAlwaysShared()
    {
        var one = new FakeLlm(GoodPlan);
        var two = new FakeLlm(GoodPlan);
        await Generator(one).GenerateAsync(Request());
        await Generator(two).GenerateAsync(new PlanRequest("contracts", new List<EntityInfo> { TestMetadata.Contract() }, new List<ResolvedRecord>(), new List<string> { "title" }, false));

        Assert.Equal(one.Calls[0].System, two.Calls[0].System);
    }

    private sealed class ThrowingLlm : ILlmClient
    {
        /// <summary>
        /// Test double model client whose JSON completion always throws, used to check how plan generation reacts to model failures.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            throw new LlmUnavailableException("Cannot reach Ollama");
        }
    }
}
