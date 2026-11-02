using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests that the plan generator runs the duplicate-row rewriter after validation and revalidates the result.
/// </summary>
public class LlmPlanGeneratorDistinctTests
{
    private const string ContractsWithLines =
        "<fetch><entity name=\"contract\"><attribute name=\"title\" /><order attribute=\"title\" descending=\"false\" />" +
        "<link-entity name=\"contractdetail\" from=\"contractid\" to=\"contractid\" alias=\"d\" link-type=\"inner\">" +
        "<filter><condition attribute=\"price\" operator=\"gt\" value=\"100\" /></filter></link-entity></entity></fetch>";

    private static string PlanJson(string fetchXml) => "{\"steps\":[{\"fetchXml\":" + System.Text.Json.JsonSerializer.Serialize(fetchXml) + "}]}";

    private static PlanRequest Request() => new PlanRequest("contracts having expensive lines",
        new List<EntityInfo> { TestMetadata.Contract() }, new List<ResolvedRecord>(), new List<string> { "title" }, false);

    private static LlmPlanGenerator Generator(FakeLlm llm, bool rewrite)
    {
        var metadata = TestMetadata.Create();
        return new LlmPlanGenerator(llm, new PlanValidator(metadata), null, rewrite ? new DistinctRewriter(metadata) : null);
    }

    /// <summary>
    /// Verifies that filter only one to many link gets distinct and the hidden ID and keeps order and top.
    /// </summary>
    [Fact]
    public async Task FilterOnlyOneToManyLink_GetsDistinctAndTheHiddenId_AndKeepsOrderAndTop()
    {
        var outcome = await Generator(new FakeLlm(PlanJson(ContractsWithLines)), true).GenerateAsync(Request());

        Assert.True(outcome.Success);
        var root = XDocument.Parse(outcome.FetchXml!).Root!;
        Assert.Equal("true", (string?)root.Attribute("distinct"));
        Assert.Equal("500", (string?)root.Attribute("top"));
        Assert.Single(root.Element("entity")!.Elements("attribute"), a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias);
        Assert.Equal("title", (string?)Assert.Single(root.Element("entity")!.Elements("order")).Attribute("attribute"));
    }

    /// <summary>
    /// Verifies that without a rewriter the plan is unchanged.
    /// </summary>
    [Fact]
    public async Task WithoutARewriter_ThePlanIsUnchanged()
    {
        var outcome = await Generator(new FakeLlm(PlanJson(ContractsWithLines)), false).GenerateAsync(Request());

        Assert.DoesNotContain("distinct", outcome.FetchXml!);
    }
}
