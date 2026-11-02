using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Prompts;

/// <summary>
/// Tests that the example FetchXML shown to the planner is built from the request's own entities.
/// </summary>
public class PlanExamplesTests
{
    private static AttributeInfo A(string name, string type = "String") => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static EntityInfo Customer(IReadOnlyList<RelationshipInfo>? relationships = null) => new EntityInfo("new_customer", "new_customers", "Customer", "new_customerid", "new_name",
        new List<AttributeInfo> { A("new_customerid", "Uniqueidentifier"), A("new_name") }, relationships ?? new List<RelationshipInfo>());

    private static EntityInfo Ticket(IReadOnlyList<RelationshipInfo>? relationships = null) => new EntityInfo("new_ticket", "new_tickets", "Ticket", "new_ticketid", "new_title",
        new List<AttributeInfo> { A("new_ticketid", "Uniqueidentifier"), A("new_title"), A("new_customerid", "Lookup") }, relationships ?? new List<RelationshipInfo>());

    private static readonly RelationshipInfo TicketToCustomer = new RelationshipInfo("new_customer_ticket", "new_ticket", "new_customerid", "new_customer", "new_customerid");

    private static async Task AssertAllValid(IReadOnlyList<string> examples, params EntityInfo[] metadata)
    {
        var validator = new PlanValidator(new FakeMetadataSource(metadata));
        foreach (var xml in examples)
        {
            var result = await validator.ValidateFetchXmlAsync(xml);
            Assert.True(result.IsValid, xml + " => " + string.Join("; ", result.Errors));
        }
    }

    /// <summary>
    /// Verifies that a single entity gets a list example and a count example.
    /// </summary>
    [Fact]
    public async Task ASingleEntity_GetsAListExampleAndACountExample()
    {
        var examples = PlanExamples.Build(new[] { Customer() });

        Assert.Equal(2, examples.Count);
        Assert.Contains("name=\"new_customer\"", examples[0]);
        Assert.Contains("aggregate=\"count\"", examples[1]);
        await AssertAllValid(examples, Customer());
    }

    /// <summary>
    /// Verifies that when the root references another entity of the request a link example with a GUID condition is added.
    /// </summary>
    [Fact]
    public async Task WhenTheRootReferencesAnotherEntityOfTheRequest_ALinkExampleWithAGuidConditionIsAdded()
    {
        var root = Ticket(new[] { TicketToCustomer });
        var other = Customer(new[] { TicketToCustomer });

        var examples = PlanExamples.Build(new[] { root, other });

        Assert.Equal(3, examples.Count);
        var link = examples.Single(e => e.Contains("link-entity"));
        Assert.Contains("<link-entity name=\"new_customer\"", link);
        Assert.Contains("alias=\"new_customer\"", link);
        Assert.Contains("<condition attribute=\"new_customerid\" operator=\"eq\" value=\"00000000-0000-0000-0000-000000000001\" />", link);
        await AssertAllValid(examples, root, other);
    }

    /// <summary>
    /// Verifies that when the root is the referenced side the link goes the other way.
    /// </summary>
    [Fact]
    public async Task WhenTheRootIsTheReferencedSide_TheLinkGoesTheOtherWay()
    {
        var root = Customer(new[] { TicketToCustomer });
        var other = Ticket(new[] { TicketToCustomer });

        var examples = PlanExamples.Build(new[] { root, other });

        var link = examples.Single(e => e.Contains("link-entity"));
        Assert.Contains("<link-entity name=\"new_ticket\"", link);
        await AssertAllValid(examples, root, other);
    }

    /// <summary>
    /// Verifies that a relationship to an entity outside the request gives no link example.
    /// </summary>
    [Fact]
    public async Task ARelationshipToAnEntityOutsideTheRequest_GivesNoLinkExample()
    {
        var root = Ticket(new[] { TicketToCustomer });

        var examples = PlanExamples.Build(new[] { root });

        Assert.DoesNotContain(examples, e => e.Contains("link-entity"));
        await AssertAllValid(examples, root);
    }

    /// <summary>
    /// Verifies that an entity without a primary name still gets valid examples.
    /// </summary>
    [Fact]
    public async Task AnEntityWithoutAPrimaryName_StillGetsValidExamples()
    {
        var bare = new EntityInfo("new_bare", "new_bares", "Bare", "new_bareid", "", new List<AttributeInfo> { A("new_bareid", "Uniqueidentifier") }, new List<RelationshipInfo>());

        var examples = PlanExamples.Build(new[] { bare });

        Assert.NotEmpty(examples);
        await AssertAllValid(examples, bare);
    }

    /// <summary>
    /// Verifies that no entities no examples.
    /// </summary>
    [Fact]
    public void NoEntities_NoExamples()
    {
        Assert.Empty(PlanExamples.Build(new List<EntityInfo>()));
    }

    /// <summary>
    /// Verifies that an example never names an attribute the entity does not have.
    /// </summary>
    [Fact]
    public async Task AnExampleNeverNamesAnAttributeTheEntityDoesNotHave()
    {
        var odd = new EntityInfo("new_odd", "new_odds", "Odd", "new_oddid", "new_missingname", new List<AttributeInfo> { A("new_oddid", "Uniqueidentifier") }, new List<RelationshipInfo>());

        var examples = PlanExamples.Build(new[] { odd });

        Assert.DoesNotContain(examples, e => e.Contains("new_missingname"));
        await AssertAllValid(examples, odd);
    }
}
