using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests the validator rules for step placeholders in dependent steps: declared step, id fields, operators and type compatibility.
/// </summary>
public class PlanValidatorPlaceholderTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static RelationshipInfo Rel(string entity, string attribute, string target) => new RelationshipInfo(entity + "_" + attribute + "_" + target, entity, attribute, target, target + "id");

    private static FakeMetadataSource Metadata()
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String"), A("numberofemployees", "Integer") },
            new List<RelationshipInfo>());
        var contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname", "String"), A("parentcustomerid", "Customer"), A("ownerid", "Owner") },
            new List<RelationshipInfo> { Rel("contact", "parentcustomerid", "account"), Rel("contact", "parentcustomerid", "contact") });
        var opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A("customerid", "Customer"), A("originatingleadid", "Lookup") },
            new List<RelationshipInfo> { Rel("opportunity", "customerid", "account"), Rel("opportunity", "customerid", "contact") });
        return new FakeMetadataSource(account, contact, opportunity);
    }

    private static Task<ValidationResult> Check(string fetchXml, StepContext? context) => new PlanValidator(Metadata()).ValidateFetchXmlAsync(fetchXml, context);

    private static StepContext Uses1(params string[] entitiesOfStep1) => new StepContext(1, new Dictionary<int, IReadOnlyCollection<string>> { [1] = entitiesOfStep1 });

    private static string Contacts(string condition) =>
        "<fetch><entity name=\"contact\"><attribute name=\"fullname\" /><filter>" + condition + "</filter></entity></fetch>";

    private const string InStep1 = "<condition attribute=\"parentcustomerid\" operator=\"in\"><value>{{step1}}</value></condition>";

    /// <summary>
    /// Verifies that an in condition on a lookup that points to the entity of the earlier step passes.
    /// </summary>
    [Fact]
    public async Task AnInCondition_OnALookupThatPointsToTheEntityOfTheEarlierStep_Passes()
    {
        var result = await Check(Contacts(InStep1), Uses1("account"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("{{step1}}", result.NormalizedFetchXml);
    }

    /// <summary>
    /// Verifies that an eq condition as the value attribute passes.
    /// </summary>
    [Fact]
    public async Task AnEqCondition_AsTheValueAttribute_Passes()
    {
        var result = await Check(Contacts("<condition attribute=\"parentcustomerid\" operator=\"eq\" value=\"{{step1}}\" />"), Uses1("account"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that a primary ID condition of the same entity as the earlier step passes.
    /// </summary>
    [Fact]
    public async Task APrimaryIdCondition_OfTheSameEntityAsTheEarlierStep_Passes()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><filter><condition attribute=\"accountid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

        var result = await Check(xml, Uses1("account"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that the placeholder inside a link entity filter resolves the linked entity.
    /// </summary>
    [Fact]
    public async Task ThePlaceholder_InsideALinkEntityFilter_ResolvesTheLinkedEntity()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" />" +
            "<link-entity name=\"opportunity\" from=\"customerid\" to=\"accountid\" alias=\"o\">" +
            "<filter><condition attribute=\"opportunityid\" operator=\"in\"><value>{{step1}}</value></condition></filter></link-entity></entity></fetch>";

        var result = await Check(xml, Uses1("opportunity"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that wrong entity type is rejected account IDs do not go to a field that only points to opportunities.
    /// </summary>
    [Fact]
    public async Task WrongEntityType_IsRejected_AccountIdsDoNotGoToAFieldThatOnlyPointsToOpportunities()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><filter><condition attribute=\"accountid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

        var result = await Check(xml, Uses1("contact"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("contact") && e.Contains("accountid"));
    }

    /// <summary>
    /// Verifies that a lookup that does not point to the earlier steps entity is rejected.
    /// </summary>
    [Fact]
    public async Task ALookupThatDoesNotPointToTheEarlierStepsEntity_IsRejected()
    {
        var xml = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"originatingleadid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

        var result = await Check(xml, Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("originatingleadid"));
    }

    /// <summary>
    /// Verifies that an operator other than eq or in is rejected.
    /// </summary>
    [Theory]
    [InlineData("ne")]
    [InlineData("like")]
    [InlineData("not-in")]
    [InlineData("gt")]
    public async Task AnOperatorOtherThanEqOrIn_IsRejected(string op)
    {
        var result = await Check(Contacts("<condition attribute=\"parentcustomerid\" operator=\"" + op + "\" value=\"{{step1}}\" />"), Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("eq") && e.Contains("in"));
    }

    /// <summary>
    /// Verifies that a non ID field is rejected.
    /// </summary>
    [Fact]
    public async Task ANonIdField_IsRejected()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><filter><condition attribute=\"name\" operator=\"eq\" value=\"{{step1}}\" /></filter></entity></fetch>";

        var result = await Check(xml, Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    /// <summary>
    /// Verifies that a field whose target cannot be verified is rejected.
    /// </summary>
    [Fact]
    public async Task AFieldWhoseTargetCannotBeVerified_IsRejected()
    {
        var result = await Check(Contacts("<condition attribute=\"ownerid\" operator=\"in\"><value>{{step1}}</value></condition>"), Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ownerid"));
    }

    /// <summary>
    /// Verifies that the placeholder must be the only value.
    /// </summary>
    [Fact]
    public async Task ThePlaceholder_MustBeTheOnlyValue()
    {
        var result = await Check(Contacts("<condition attribute=\"parentcustomerid\" operator=\"in\"><value>{{step1}}</value><value>37870e9c-c7c7-f111-b4df-00155d01f303</value></condition>"), Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("only value"));
    }

    /// <summary>
    /// Verifies that referencing any step other than the declared earlier one is rejected.
    /// </summary>
    [Theory]
    [InlineData("{{step2}}")]
    [InlineData("{{step7}}")]
    public async Task ReferencingAnyStepOtherThanTheDeclaredEarlierOne_IsRejected(string placeholder)
    {
        var result = await Check(Contacts("<condition attribute=\"parentcustomerid\" operator=\"in\"><value>" + placeholder + "</value></condition>"), Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("step"));
    }

    /// <summary>
    /// Verifies that a placeholder without any step context is rejected.
    /// </summary>
    [Fact]
    public async Task APlaceholder_WithoutAnyStepContext_IsRejected()
    {
        var result = await Check(Contacts(InStep1), null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("placeholder"));
    }

    /// <summary>
    /// Verifies that a placeholder in a step that does not use an earlier one is rejected.
    /// </summary>
    [Fact]
    public async Task APlaceholder_InAStepThatDoesNotUseAnEarlierOne_IsRejected()
    {
        var result = await Check(Contacts(InStep1), new StepContext(null, new Dictionary<int, IReadOnlyCollection<string>>()));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Verifies that a dependent step that never uses the placeholder is rejected.
    /// </summary>
    [Fact]
    public async Task ADependentStep_ThatNeverUsesThePlaceholder_IsRejected()
    {
        var result = await Check("<fetch><entity name=\"contact\"><attribute name=\"fullname\" /></entity></fetch>", Uses1("account"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("{{step1}}"));
    }

    /// <summary>
    /// Verifies that a placeholder on a non ID field is rejected.
    /// </summary>
    [Fact]
    public async Task APlaceholderOnANonIdField_IsRejected()
    {
        var result = await Check(Contacts("<condition attribute=\"fullname\" operator=\"eq\" value=\"{{step1}}\" />"), Uses1("account"));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Verifies that without a context and without placeholders nothing changes.
    /// </summary>
    [Fact]
    public async Task WithoutAContext_AndWithoutPlaceholders_NothingChanges()
    {
        var result = await Check(Contacts("<condition attribute=\"fullname\" operator=\"eq\" value=\"Freya\" />"), null);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that IDs that may be accounts or contacts are accepted where the field points to either.
    /// </summary>
    [Fact]
    public async Task IdsThatMayBeAccountsOrContacts_AreAcceptedWhereTheFieldPointsToEither()
    {
        var result = await Check(Contacts(InStep1), Uses1("account", "contact"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that IDs that may be accounts or contacts are rejected where the field points to neither.
    /// </summary>
    [Fact]
    public async Task IdsThatMayBeAccountsOrContacts_AreRejectedWhereTheFieldPointsToNeither()
    {
        var xml = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"originatingleadid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

        var result = await Check(xml, Uses1("account", "contact"));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Verifies that when the ID entities are unknown the placeholder is rejected.
    /// </summary>
    [Fact]
    public async Task WhenTheIdEntitiesAreUnknown_ThePlaceholderIsRejected()
    {
        var result = await Check(Contacts(InStep1), Uses1());

        Assert.False(result.IsValid);
    }

    private const string Aggregate = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />{0}<order alias=\"n\" descending=\"true\" /></entity></fetch>";

    private static StepContext Produces() => new StepContext(null, new Dictionary<int, IReadOnlyCollection<string>>(), ProducesIds: true);

    /// <summary>
    /// Verifies that an aggregate producer step must group by an ID or lookup with the row ID alias.
    /// </summary>
    [Fact]
    public async Task AnAggregateProducerStep_MustGroupByAnIdOrLookupWithTheRowIdAlias()
    {
        var withId = await Check(string.Format(Aggregate, "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" />"), Produces());
        var byName = await Check(string.Format(Aggregate, "<attribute name=\"name\" groupby=\"true\" alias=\"who\" />"), Produces());

        Assert.True(withId.IsValid, string.Join("; ", withId.Errors));
        Assert.False(byName.IsValid);
        Assert.Contains(byName.Errors, e => e.Contains("_rowid") && e.Contains("groupby"));
    }

    /// <summary>
    /// Verifies that the row ID group by must be an ID or lookup field not an arbitrary one.
    /// </summary>
    [Fact]
    public async Task TheRowIdGroupBy_MustBeAnIdOrLookupField_NotAnArbitraryOne()
    {
        var result = await Check(string.Format(Aggregate, "<attribute name=\"name\" groupby=\"true\" alias=\"_rowid\" />"), Produces());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    /// <summary>
    /// Verifies that a producer that is not an aggregate needs nothing the code adds the ID column.
    /// </summary>
    [Fact]
    public async Task AProducerThatIsNotAnAggregate_NeedsNothing_TheCodeAddsTheIdColumn()
    {
        var result = await Check("<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", Produces());

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that the row ID alias without a producer context changes nothing.
    /// </summary>
    [Fact]
    public async Task TheRowIdAlias_WithoutAProducerContext_ChangesNothing()
    {
        var result = await Check(string.Format(Aggregate, "<attribute name=\"name\" groupby=\"true\" alias=\"_rowid\" />"), null);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }
}
