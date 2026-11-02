using System.Text;
using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Knowledge;

/// <summary>
/// Tests how company knowledge enters model prompts: only when present, otherwise the prompt stays identical.
/// </summary>
public class KnowledgeInPromptsTests
{
    private const string Plan = "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"productid\\\" /><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";
    private const string Analysis = """{"primaryEntity":"product","relatedEntities":[],"mentions":[],"columns":[],"aggregate":false}""";

    private static readonly IReadOnlyList<EntitySummary> Entities = new List<EntitySummary> { new EntitySummary("product", "products", "Product") };

    private static PlanRequest PlanRequestWith(IReadOnlyList<string>? knowledge) => new PlanRequest(
        "all products", new List<EntityInfo> { TestMetadata.Product() }, new List<ResolvedRecord>(), new List<string> { "productid", "name" }, false, knowledge);

    /// <summary>
    /// Verifies that with no knowledge nothing is added.
    /// </summary>
    [Fact]
    public void NoKnowledge_AddsNothing()
    {
        var sb = new StringBuilder("start");

        KnowledgePrompt.Append(sb, null);
        KnowledgePrompt.Append(sb, new List<string>());

        Assert.Equal("start", sb.ToString());
    }

    /// <summary>
    /// Verifies that knowledge is marked as information, not instructions, and every note is kept.
    /// </summary>
    [Fact]
    public void Knowledge_IsMarkedAsInformationNotInstructions_AndKeepsEveryNote()
    {
        var sb = new StringBuilder();

        KnowledgePrompt.Append(sb, new[] { "Aktif müşteri\nSon 12 ayda siparişi olan hesap.", "KOBİ\nnew_musteriSegmenti = 2" });

        var text = sb.ToString();
        Assert.Contains("not instructions", text);
        Assert.Contains("Son 12 ayda siparişi olan hesap.", text);
        Assert.Contains("new_musteriSegmenti = 2", text);
    }

    /// <summary>
    /// Verifies that the plan prompt is identical to the old one when there is no knowledge.
    /// </summary>
    [Fact]
    public async Task ThePlanPrompt_IsIdenticalToTheOldOne_WhenThereIsNoKnowledge()
    {
        var without = new FakeLlm(Plan);
        var empty = new FakeLlm(Plan);
        await new LlmPlanGenerator(without, new PlanValidator(TestMetadata.Create())).GenerateAsync(PlanRequestWith(null));
        await new LlmPlanGenerator(empty, new PlanValidator(TestMetadata.Create())).GenerateAsync(PlanRequestWith(new List<string>()));

        Assert.Equal(without.Calls[0].User, empty.Calls[0].User);
        Assert.DoesNotContain("Company knowledge", without.Calls[0].User);
    }

    /// <summary>
    /// Verifies that the plan prompt contains the knowledge before the resolved records.
    /// </summary>
    [Fact]
    public async Task ThePlanPrompt_ContainsTheKnowledge_BeforeTheResolvedRecords()
    {
        var llm = new FakeLlm(Plan);
        var request = PlanRequestWith(new[] { "Aktif müşteri\nSon 12 ayda siparişi olan hesap." });

        await new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create())).GenerateAsync(request);

        Assert.Contains("Company knowledge", llm.Calls[0].User);
        Assert.Contains("Son 12 ayda siparişi olan hesap.", llm.Calls[0].User);
        Assert.DoesNotContain("Company knowledge", llm.Calls[0].System);
    }

    /// <summary>
    /// Verifies that the analysis prompt contains the knowledge between the entities and the question.
    /// </summary>
    [Fact]
    public async Task TheAnalysisPrompt_ContainsTheKnowledge_BetweenTheEntitiesAndTheQuestion()
    {
        var llm = new FakeLlm(Analysis);

        await new LlmQuestionAnalyzer(llm).AnalyzeAsync("kobi müşterileri", Entities, new[] { "KOBİ\nnew_musteriSegmenti = 2" });

        var user = llm.Calls[0].User;
        Assert.True(user.IndexOf("product | Product", StringComparison.Ordinal) < user.IndexOf("Company knowledge", StringComparison.Ordinal));
        Assert.True(user.IndexOf("Company knowledge", StringComparison.Ordinal) < user.IndexOf("Question: kobi", StringComparison.Ordinal));
        Assert.Contains("new_musteriSegmenti = 2", user);
    }

    /// <summary>
    /// Verifies that the analysis prompt is identical to the old one when there is no knowledge.
    /// </summary>
    [Fact]
    public async Task TheAnalysisPrompt_IsIdenticalToTheOldOne_WhenThereIsNoKnowledge()
    {
        var old = new FakeLlm(Analysis);
        var withEmpty = new FakeLlm(Analysis);
        await new LlmQuestionAnalyzer(old).AnalyzeAsync("all products", Entities);
        await new LlmQuestionAnalyzer(withEmpty).AnalyzeAsync("all products", Entities, new List<string>());

        Assert.Equal(old.Calls[0].System, withEmpty.Calls[0].System);
        Assert.Equal(old.Calls[0].User, withEmpty.Calls[0].User);
        Assert.Contains("Entities (logical name | label):\nproduct | Product\n", old.Calls[0].System);
        Assert.Equal("Question: all products", old.Calls[0].User);
    }
}
