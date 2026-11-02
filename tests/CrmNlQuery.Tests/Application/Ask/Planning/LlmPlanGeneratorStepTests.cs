using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests planning of dependent steps with a placeholder and producer steps that group by row id.
/// </summary>
public class LlmPlanGeneratorStepTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static RelationshipInfo Rel(string entity, string attribute, string target) => new RelationshipInfo(entity + "_" + attribute + "_" + target, entity, attribute, target, target + "id");

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
        new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname", "String"), A("parentcustomerid", "Customer") },
        new List<RelationshipInfo> { Rel("contact", "parentcustomerid", "account"), Rel("contact", "parentcustomerid", "contact") });

    private static readonly EntityInfo Opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A("customerid", "Customer") },
        new List<RelationshipInfo> { Rel("opportunity", "customerid", "account"), Rel("opportunity", "customerid", "contact") });

    private static FakeMetadataSource Metadata() => new FakeMetadataSource(Account, Contact, Opportunity);

    private static LlmPlanGenerator Generator(FakeLlm llm) => new LlmPlanGenerator(llm, new PlanValidator(Metadata()));

    private static string PlanJson(string fetchXml) => "{\"steps\":[{\"fetchXml\":" + System.Text.Json.JsonSerializer.Serialize(fetchXml) + "}]}";

    private const string ContactsOfStep1 =
        "<fetch><entity name=\"contact\"><attribute name=\"fullname\" /><filter><condition attribute=\"parentcustomerid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

    private static PlanRequest Dependent(StepPlanInfo? step) =>
        new PlanRequest("the contacts of those accounts", new List<EntityInfo> { Contact }, new List<ResolvedRecord>(), new List<string> { "fullname" }, false, null, null, step);

    private static readonly StepPlanInfo UsesAccountsOfStep1 = new StepPlanInfo(1, new[] { "account" }, false);

    /// <summary>
    /// Verifies that a dependent step prompt tells the model the exact placeholder and never shows IDs.
    /// </summary>
    [Fact]
    public async Task ADependentStep_Prompt_TellsTheModelTheExactPlaceholder_AndNeverShowsIds()
    {
        var llm = new FakeLlm(PlanJson(ContactsOfStep1));

        var outcome = await Generator(llm).GenerateAsync(Dependent(UsesAccountsOfStep1));

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var user = llm.Calls[0].User;
        Assert.Contains("{{step1}}", user);
        Assert.Contains("step 1", user);
        Assert.Contains("account", user);
        Assert.Contains("operator=\"in\"", user);
        Assert.Contains("never write", user.ToLowerInvariant());
        Assert.DoesNotMatch("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}", user);
    }

    /// <summary>
    /// Verifies that a dependent step prompt names the lookup from the relationship list.
    /// </summary>
    [Fact]
    public async Task ADependentStep_Prompt_NamesTheLookupFromTheRelationshipList()
    {
        var llm = new FakeLlm(PlanJson(ContactsOfStep1));
        var request = Dependent(UsesAccountsOfStep1) with { Entities = new List<EntityInfo> { Contact, Account } };

        await Generator(llm).GenerateAsync(request);

        Assert.Contains("The relationship to those records is: contact.parentcustomerid.", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that a dependent step prompt has no relationship line when no relationship is known.
    /// </summary>
    [Fact]
    public async Task ADependentStep_Prompt_HasNoRelationshipLine_WhenNoRelationshipIsKnown()
    {
        var llm = new FakeLlm(PlanJson(ContactsOfStep1));

        await Generator(llm).GenerateAsync(Dependent(new StepPlanInfo(1, new[] { "opportunity" }, false)));

        Assert.DoesNotContain("The relationship to those records", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that a dependent plan is accepted with the step info and rejected without it.
    /// </summary>
    [Fact]
    public async Task ADependentPlan_IsAccepted_WithTheStepInfo_AndRejectedWithout()
    {
        var accepted = await Generator(new FakeLlm(PlanJson(ContactsOfStep1))).GenerateAsync(Dependent(UsesAccountsOfStep1));
        var llm = new FakeLlm(PlanJson(ContactsOfStep1), PlanJson(ContactsOfStep1), PlanJson(ContactsOfStep1));
        var rejected = await Generator(llm).GenerateAsync(Dependent(null));

        Assert.True(accepted.Success);
        Assert.Contains("{{step1}}", accepted.FetchXml);
        Assert.False(rejected.Success);
    }

    /// <summary>
    /// Verifies that a placeholder on a field that cannot hold those ids is fed back to the model and then fixed.
    /// </summary>
    [Fact]
    public async Task APlaceholderOnAFieldThatCannotHoldThoseIds_IsFedBackToTheModel_ThenFixed()
    {
        var wrong = "<fetch><entity name=\"contact\"><attribute name=\"fullname\" /><filter><condition attribute=\"fullname\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";
        var llm = new FakeLlm(PlanJson(wrong), PlanJson(ContactsOfStep1));

        var outcome = await Generator(llm).GenerateAsync(Dependent(UsesAccountsOfStep1));

        Assert.True(outcome.Success);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("fullname", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that a dependent step never gets a distinct rewrite that drops the placeholder.
    /// </summary>
    [Fact]
    public async Task ADependentStep_NeverGetsADistinctRewriteThatDropsThePlaceholder()
    {
        var generator = new LlmPlanGenerator(new FakeLlm(PlanJson(ContactsOfStep1)), new PlanValidator(Metadata()), null, new DistinctRewriter(Metadata()));

        var outcome = await generator.GenerateAsync(Dependent(UsesAccountsOfStep1));

        Assert.True(outcome.Success);
        Assert.Contains("{{step1}}", outcome.FetchXml);
    }

    private const string CountByCustomer =
        "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
        "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /><order alias=\"n\" descending=\"true\" /></entity></fetch>";

    private const string CountByName =
        "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
        "<attribute name=\"name\" groupby=\"true\" alias=\"who\" /><order alias=\"n\" descending=\"true\" /></entity></fetch>";

    private static PlanRequest Producer() =>
        new PlanRequest("the account with the most open opportunities", new List<EntityInfo> { Opportunity }, new List<ResolvedRecord>(), new List<string>(), true, null, null,
            new StepPlanInfo(null, new string[0], true));

    /// <summary>
    /// Verifies that a producer step prompt explains grouping by the row id.
    /// </summary>
    [Fact]
    public async Task AProducerStep_Prompt_ExplainsTheRowIdGroupBy()
    {
        var llm = new FakeLlm(PlanJson(CountByCustomer));

        var outcome = await Generator(llm).GenerateAsync(Producer());

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.Contains("_rowid", llm.Calls[0].User);
        Assert.Contains("groupby", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that a producer aggregate grouped by name is fed back and then fixed.
    /// </summary>
    [Fact]
    public async Task AProducerAggregateGroupedByName_IsFedBack_ThenFixed()
    {
        var llm = new FakeLlm(PlanJson(CountByName), PlanJson(CountByCustomer));

        var outcome = await Generator(llm).GenerateAsync(Producer());

        Assert.True(outcome.Success);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("_rowid", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that without step info the prompt has no step section.
    /// </summary>
    [Fact]
    public async Task WithoutStepInfo_ThePromptHasNoStepSection()
    {
        var llm = new FakeLlm(PlanJson("<fetch><entity name=\"contact\"><attribute name=\"fullname\" /></entity></fetch>"));

        await Generator(llm).GenerateAsync(Dependent(null));

        Assert.DoesNotContain("{{step", llm.Calls[0].User);
        Assert.DoesNotContain("_rowid", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that the step info is part of the plan cache key.
    /// </summary>
    [Fact]
    public async Task TheStepInfo_IsPartOfThePlanCacheKey()
    {
        var calls = 0;
        var inner = new CountingPlanner(() => calls++);
        var cache = new CachingPlanGenerator(inner, TimeSpan.FromHours(1), 100, null);

        await cache.GenerateAsync(Dependent(null));
        await cache.GenerateAsync(Dependent(UsesAccountsOfStep1));
        await cache.GenerateAsync(Dependent(new StepPlanInfo(1, new[] { "contact" }, false)));
        await cache.GenerateAsync(Dependent(UsesAccountsOfStep1));

        Assert.Equal(3, calls);
    }

    private sealed class CountingPlanner : IPlanGenerator
    {
        private readonly Action _onCall;
        public CountingPlanner(Action onCall) { _onCall = onCall; }
        /// <summary>
        /// Fake planner that counts how many times it was called.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            _onCall();
            return Task.FromResult(new PlanOutcome(true, "<fetch />", new List<string>()));
        }
    }
}
