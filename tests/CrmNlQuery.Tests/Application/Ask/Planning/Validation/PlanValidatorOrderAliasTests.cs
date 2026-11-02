using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests that an aggregate query's order alias must match an attribute alias, and related aggregate alias rules.
/// </summary>
public class PlanValidatorOrderAliasTests
{
    private static Task<ValidationResult> Check(string fetchXml) => new PlanValidator(TestMetadata.Create()).ValidateFetchXmlAsync(fetchXml);

    private static string Aggregate(string measureAlias, string orderAlias) =>
        "<fetch aggregate=\"true\"><entity name=\"contractdetail\"><attribute name=\"contractdetailid\" aggregate=\"count\" alias=\"" + measureAlias + "\" />" +
        "<attribute name=\"contractid\" groupby=\"true\" alias=\"who\" /><order alias=\"" + orderAlias + "\" descending=\"true\" /></entity></fetch>";

    /// <summary>
    /// Verifies that an order alias matching an attribute alias passes.
    /// </summary>
    [Fact]
    public async Task AnOrderAlias_ThatMatchesAnAttributeAlias_Passes()
    {
        var result = await Check(Aggregate("n", "n"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that an order alias matching no attribute alias is rejected, listing the aliases that exist.
    /// </summary>
    [Fact]
    public async Task AnOrderAlias_ThatMatchesNoAttributeAlias_IsRejected_WithTheAliasesThatExist()
    {
        var result = await Check(Aggregate("opencount", "count"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'count'", error);
        Assert.Contains("opencount", error);
    }

    /// <summary>
    /// Verifies that an order alias inside the same link-entity as the measure passes.
    /// </summary>
    [Fact]
    public async Task TheOrderAlias_InsideTheSameLinkEntityAsTheMeasure_Passes()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"contractdetail\"><attribute name=\"contractid\" groupby=\"true\" alias=\"who\" />" +
            "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"c\"><attribute name=\"contractid\" aggregate=\"count\" alias=\"n\" /><order alias=\"n\" descending=\"true\" /></link-entity>" +
            "</entity></fetch>";

        var result = await Check(xml);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that an order alias at the root is rejected when the measure is inside a link-entity, with a hint to move it.
    /// </summary>
    [Fact]
    public async Task TheOrderAlias_AtTheRoot_WhenTheMeasureIsInsideALinkEntity_IsRejected_WithAHintToMoveIt()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"contractdetail\"><attribute name=\"contractid\" groupby=\"true\" alias=\"who\" />" +
            "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"c\"><attribute name=\"contractid\" aggregate=\"count\" alias=\"n\" /></link-entity>" +
            "<order alias=\"n\" descending=\"true\" /></entity></fetch>";

        var result = await Check(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'n'", error);
        Assert.Contains("same", error);
    }

    /// <summary>
    /// Verifies that the plan prompt tells the model where the order alias belongs.
    /// </summary>
    [Fact]
    public async Task ThePlanPrompt_TellsTheModelWhereTheOrderAliasBelongs()
    {
        var llm = new FakeLlm("{\"needsSteps\":true,\"steps\":[]}");
        var request = new CrmNlQuery.Domain.Ask.Entities.PlanRequest("q", new List<CrmNlQuery.Domain.Metadata.Entities.EntityInfo> { TestMetadata.Product() },
            new List<CrmNlQuery.Domain.Ask.Entities.ResolvedRecord>(), new List<string> { "name" }, true);

        await new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create())).GenerateAsync(request);

        Assert.Contains("same <entity> or <link-entity>", llm.Calls[0].System);
    }

    /// <summary>
    /// Verifies that in an aggregate query every attribute needs an alias.
    /// </summary>
    [Fact]
    public async Task InAnAggregateQuery_EveryAttributeNeedsAnAlias()
    {
        var withoutAlias = "<fetch aggregate=\"true\"><entity name=\"contractdetail\"><attribute name=\"contractdetailid\" aggregate=\"count\" alias=\"n\" /><attribute name=\"contractid\" groupby=\"true\" /></entity></fetch>";
        var inLink = "<fetch aggregate=\"true\"><entity name=\"contractdetail\"><attribute name=\"contractdetailid\" aggregate=\"count\" alias=\"n\" />" +
            "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"c\"><attribute name=\"title\" groupby=\"true\" /></link-entity></entity></fetch>";

        var rootResult = await Check(withoutAlias);
        var linkResult = await Check(inLink);

        Assert.False(rootResult.IsValid);
        Assert.Contains(rootResult.Errors, e => e.Contains("alias") && e.Contains("contractid"));
        Assert.False(linkResult.IsValid);
        Assert.Contains(linkResult.Errors, e => e.Contains("title"));
    }

    /// <summary>
    /// Verifies that aggregate attributes that all have aliases pass and plain queries are not affected.
    /// </summary>
    [Fact]
    public async Task InAnAggregateQuery_AttributesThatAllHaveAliases_Pass_AndPlainQueriesAreNotAffected()
    {
        var aggregate = await Check(Aggregate("n", "n"));
        var plain = await Check("<fetch><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>");

        Assert.True(aggregate.IsValid, string.Join("; ", aggregate.Errors));
        Assert.True(plain.IsValid, string.Join("; ", plain.Errors));
    }

    /// <summary>
    /// Verifies that ordering by an attribute name is not affected.
    /// </summary>
    [Fact]
    public async Task OrderByAnAttributeName_IsNotAffected()
    {
        var xml = "<fetch><entity name=\"product\"><attribute name=\"name\" /><order attribute=\"name\" /></entity></fetch>";

        var result = await Check(xml);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }
}
