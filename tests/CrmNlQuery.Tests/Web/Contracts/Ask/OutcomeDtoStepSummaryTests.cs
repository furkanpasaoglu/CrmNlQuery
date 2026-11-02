using System.Text.Json;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Tests.Web.Contracts.Ask;

/// <summary>
/// Tests the step summary fields of the outcome DTO.
/// </summary>
public class OutcomeDtoStepSummaryTests
{
    private static JsonElement Serialize(AskOutcome outcome)
    {
        var json = JsonSerializer.Serialize(OutcomeDto.From(outcome), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Verifies that a chain answer sends step summaries with names and no ids.
    /// </summary>
    [Fact]
    public void AChainAnswer_SendsTheStepSummaries_WithNames_AndNoIds()
    {
        var table = new ResultTable(new List<string> { "fullname" }, new List<IReadOnlyList<string>> { new List<string> { "Freya" } });
        var summary = new StepSummary(1, "the account with the most open opportunities", 1, new List<string> { "n" },
            new List<IReadOnlyList<string>> { new List<string> { "3" } }, new List<string> { "Brightwave Appliances Ltd" });

        var json = Serialize(new AnswerOutcome("contact", "u", "<fetch/>", table, false) { StepSummaries = new[] { summary }, FromChain = true });

        Assert.True(json.GetProperty("fromChain").GetBoolean());
        var step = json.GetProperty("stepSummaries")[0];
        Assert.Equal(1, step.GetProperty("number").GetInt32());
        Assert.Equal("Brightwave Appliances Ltd", step.GetProperty("names")[0].GetString());
        Assert.Equal("3", step.GetProperty("preview")[0][0].GetString());
        Assert.DoesNotMatch("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}", json.GetRawText());
    }

    /// <summary>
    /// Verifies that a normal answer has no summaries and is not from a chain.
    /// </summary>
    [Fact]
    public void ANormalAnswer_HasNoSummaries_AndIsNotFromAChain()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>());

        var json = Serialize(new AnswerOutcome("product", "u", "<fetch/>", table, false));

        Assert.False(json.GetProperty("fromChain").GetBoolean());
        Assert.Equal(0, json.GetProperty("stepSummaries").GetArrayLength());
    }
}
