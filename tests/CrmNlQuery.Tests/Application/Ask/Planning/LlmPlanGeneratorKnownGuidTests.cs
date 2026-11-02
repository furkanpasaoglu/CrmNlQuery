using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Holds tests that every record GUID in a query must be a known record.
/// </summary>
public class LlmPlanGeneratorKnownGuidTests
{
    private const string KnownId = "37870e9c-c7c7-f111-b4df-00155d01f303";
    private const string FakeId = "00000000-0000-0000-0000-000000000001";

    private static string Plan(string id) => "{\"steps\":[{\"fetchXml\":" + System.Text.Json.JsonSerializer.Serialize(
        "<fetch><entity name=\"product\"><attribute name=\"name\" /><filter><condition attribute=\"productid\" operator=\"eq\" value=\"" + id + "\" /></filter></entity></fetch>") + "}]}";

    private static PlanRequest Request(string question = "this product", IReadOnlyList<ResolvedRecord>? resolved = null, PreviousQuery? previous = null) =>
        new PlanRequest(question, new List<EntityInfo> { TestMetadata.Product() }, resolved ?? new List<ResolvedRecord>(), new List<string> { "name" }, false, null, previous);

    private static LlmPlanGenerator Generator(FakeLlm llm) => new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create()));

    /// <summary>
    /// Verifies that an invented GUID is rejected and the model is told why.
    /// </summary>
    [Fact]
    public async Task AnInventedGuid_IsRejected_AndTheModelIsToldWhy()
    {
        var llm = new FakeLlm(Plan(FakeId), Plan(FakeId), Plan(FakeId));

        var outcome = await Generator(llm).GenerateAsync(Request());

        Assert.False(outcome.Success);
        Assert.Equal(LlmPlanGenerator.MaxAttempts, llm.Calls.Count);
        Assert.Contains(outcome.Errors, e => e.Contains(FakeId) && e.Contains("known record"));
        Assert.Contains(FakeId, llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that aGuid of aResolved record is accepted.
    /// </summary>
    [Fact]
    public async Task AGuidOfAResolvedRecord_IsAccepted()
    {
        var resolved = new List<ResolvedRecord> { new ResolvedRecord("x", "product", Guid.Parse(KnownId), "Chair") };

        var outcome = await Generator(new FakeLlm(Plan(KnownId))).GenerateAsync(Request(resolved: resolved));

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
    }

    /// <summary>
    /// Verifies that aGuid the user wrote in the question is accepted.
    /// </summary>
    [Fact]
    public async Task AGuidTheUserWroteInTheQuestion_IsAccepted()
    {
        var outcome = await Generator(new FakeLlm(Plan(KnownId))).GenerateAsync(Request("product " + KnownId));

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
    }

    /// <summary>
    /// Verifies that aGuid from the previous query is accepted for follow ups.
    /// </summary>
    [Fact]
    public async Task AGuidFromThePreviousQuery_IsAccepted_ForFollowUps()
    {
        var previous = new PreviousQuery("q", "product", Plan(KnownId).Replace("\u0022", "\""));

        var outcome = await Generator(new FakeLlm(Plan(KnownId))).GenerateAsync(Request(previous: previous));

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
    }

    /// <summary>
    /// Verifies that aQuery without any guid is untouched.
    /// </summary>
    [Fact]
    public async Task AQueryWithoutAnyGuid_IsUntouched()
    {
        var plain = "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";

        Assert.True((await Generator(new FakeLlm(plain)).GenerateAsync(Request())).Success);
    }
}
