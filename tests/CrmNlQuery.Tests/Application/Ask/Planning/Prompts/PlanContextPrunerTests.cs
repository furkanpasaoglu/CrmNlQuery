using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Prompts;

/// <summary>
/// Holds tests for pruning the entity blocks given to the planner.
/// </summary>
public class PlanContextPrunerTests
{
    private static AttributeInfo A(string name, string type = "String", string? label = null, params (int, string)[] options)
        => new AttributeInfo(name, label ?? name, type, options.Select(o => new OptionInfo(o.Item1, o.Item2)).ToList());

    private static RelationshipInfo Rel(string referencing, string attribute, string referenced)
        => new RelationshipInfo($"{referencing}_{attribute}", referencing, attribute, referenced, referenced + "id");

    private static EntityInfo Account(IEnumerable<AttributeInfo> attributes, params RelationshipInfo[] relationships)
        => new EntityInfo("account", "accounts", "Account", "accountid", "name", attributes.ToList(), relationships.ToList());

    private static PlanRequest Request(EntityInfo entity, string question = "accounts", string[]? columns = null, PreviousQuery? previous = null, IReadOnlyList<string>? knowledge = null, params EntityInfo[] more)
        => new PlanRequest(question, new[] { entity }.Concat(more).ToList(), new List<ResolvedRecord>(), columns ?? new[] { "name" }, false, knowledge, previous);

    private static string[] Names(EntityInfo e) => e.Attributes.Select(a => a.LogicalName).ToArray();

    /// <summary>
    /// Verifies that the status name helpers are dropped only when their base attribute exists.
    /// </summary>
    [Fact]
    public void TheStatusNameHelpers_AreDropped_OnlyWhenTheirBaseAttributeExists()
    {
        var entity = Account(new[]
        {
            A("accountid", "Uniqueidentifier"), A("name"), A("statecode", "State"), A("statecodename", "Virtual"),
            A("preferredcontactmethodcode", "Picklist"), A("preferredcontactmethodcodename", "Virtual"),
            A("new_features", "Virtual"),
            A("orphanname", "Virtual"),
        });

        var names = Names(PlanContextPruner.Prune(Request(entity)).Single());

        Assert.DoesNotContain("statecodename", names);
        Assert.DoesNotContain("preferredcontactmethodcodename", names);
        Assert.Contains("new_features", names);
        Assert.Contains("orphanname", names);
    }

    /// <summary>
    /// Verifies that yomi money base and tracking attributes are dropped.
    /// </summary>
    [Fact]
    public void YomiMoneyBaseAndTrackingAttributes_AreDropped()
    {
        var entity = Account(new[]
        {
            A("accountid", "Uniqueidentifier"), A("name"), A("yominame"), A("masteraccountidyominame"),
            A("revenue", "Money"), A("revenue_base", "Money"), A("versionnumber", "BigInt"), A("importsequencenumber", "Integer"),
            A("timezoneruleversionnumber", "Integer"), A("utcconversiontimezonecode", "Integer"), A("overriddencreatedon", "DateTime"),
            A("processid", "Uniqueidentifier"), A("stageid", "Uniqueidentifier"), A("traversedpath", "String"), A("exchangerate", "Decimal"),
            A("createdonbehalfby", "Lookup"), A("modifiedonbehalfby", "Lookup"), A("createdbyexternalparty", "Lookup"), A("modifiedbyexternalparty", "Lookup"),
        });

        var names = Names(PlanContextPruner.Prune(Request(entity)).Single());

        Assert.Equal(new[] { "accountid", "name", "revenue" }, names);
    }

    /// <summary>
    /// Verifies that the money base is kept when there is no matching money attribute.
    /// </summary>
    [Fact]
    public void TheMoneyBaseIsKept_WhenThereIsNoMatchingMoneyAttribute()
    {
        var entity = Account(new[] { A("accountid", "Uniqueidentifier"), A("name"), A("something_base", "Money") });

        Assert.Contains("something_base", Names(PlanContextPruner.Prune(Request(entity)).Single()));
    }

    /// <summary>
    /// Verifies that the pointless types are dropped but every ordinary type stays.
    /// </summary>
    [Fact]
    public void ThePointlessTypes_AreDropped_ButEveryOrdinaryTypeStays()
    {
        var entity = Account(new[]
        {
            A("accountid", "Uniqueidentifier"), A("name"), A("customeridtype", "EntityName"), A("entityimage", "Virtual"), A("entityimageid", "Uniqueidentifier"),
            A("numberofemployees", "Integer"), A("revenue", "Money"), A("createdon", "DateTime"), A("donotemail", "Boolean"), A("description", "Memo"),
            A("industrycode", "Picklist", "Industry", (1, "Accounting")), A("parentaccountid", "Lookup"), A("ownerid", "Owner"),
        });

        var names = Names(PlanContextPruner.Prune(Request(entity)).Single());

        Assert.DoesNotContain("customeridtype", names);
        Assert.Equal(new[] { "accountid", "name", "entityimage", "entityimageid", "numberofemployees", "revenue", "createdon", "donotemail", "description", "industrycode", "parentaccountid", "ownerid" }, names);
    }

    /// <summary>
    /// Verifies that the second and third address are dropped unless the question columns or previous query mention them.
    /// </summary>
    [Fact]
    public void TheSecondAndThirdAddress_AreDropped_UnlessTheQuestionColumnsOrPreviousQueryMentionThem()
    {
        AttributeInfo[] Attributes() => new[] { A("accountid", "Uniqueidentifier"), A("name"), A("address1_city"), A("address2_city"), A("address3_line1") };

        Assert.Equal(new[] { "accountid", "name", "address1_city" }, Names(PlanContextPruner.Prune(Request(Account(Attributes()))).Single()));
        Assert.Contains("address2_city", Names(PlanContextPruner.Prune(Request(Account(Attributes()), question: "accounts whose second address is in Paris")).Single()));
        Assert.Contains("address2_city", Names(PlanContextPruner.Prune(Request(Account(Attributes()), question: "ikinci adresi Paris olan firmalar")).Single()));
        Assert.Contains("address2_city", Names(PlanContextPruner.Prune(Request(Account(Attributes()), question: "accounts", columns: new[] { "name", "address2_city" })).Single()));
        Assert.Contains("address2_city", Names(PlanContextPruner.Prune(Request(Account(Attributes()),
            previous: new PreviousQuery("accounts", "account", "<fetch><entity name=\"account\"><attribute name=\"address2_city\" /></entity></fetch>"))).Single()));
        Assert.Contains("address3_line1", Names(PlanContextPruner.Prune(Request(Account(Attributes()), question: "show address3_line1")).Single()));
    }

    /// <summary>
    /// Verifies that what the question names explicitly is never dropped.
    /// </summary>
    [Fact]
    public void WhatTheQuestionNamesExplicitly_IsNeverDropped()
    {
        var entity = Account(new[] { A("accountid", "Uniqueidentifier"), A("name"), A("yominame"), A("versionnumber", "BigInt"), A("statecodename", "Virtual"), A("statecode", "State") });

        var names = Names(PlanContextPruner.Prune(Request(entity, question: "show versionnumber and yominame")).Single());

        Assert.Contains("versionnumber", names);
        Assert.Contains("yominame", names);
    }

    /// <summary>
    /// Verifies that requested columns primary attributes and the usual business filters are never dropped.
    /// </summary>
    [Fact]
    public void RequestedColumns_PrimaryAttributes_AndTheUsualBusinessFilters_AreNeverDropped()
    {
        var entity = new EntityInfo("thing", "things", "Thing", "versionnumber", "yominame",
            new List<AttributeInfo> { A("versionnumber", "BigInt"), A("yominame"), A("importsequencenumber", "Integer"), A("statecode", "State"), A("statuscode", "Status"), A("ownerid", "Owner"), A("createdby", "Lookup") },
            new List<RelationshipInfo>());

        var names = Names(PlanContextPruner.Prune(Request(entity, columns: new[] { "importsequencenumber" })).Single());

        Assert.Equal(new[] { "versionnumber", "yominame", "importsequencenumber", "statecode", "statuscode", "ownerid", "createdby" }, names);
    }

    /// <summary>
    /// Verifies that relationships are kept only when both ends are entities of the request.
    /// </summary>
    [Fact]
    public void Relationships_AreKept_OnlyWhenBothEndsAreEntitiesOfTheRequest()
    {
        var account = Account(new[] { A("accountid", "Uniqueidentifier"), A("name"), A("parentaccountid", "Lookup") },
            Rel("account", "parentaccountid", "account"),
            Rel("contact", "parentcustomerid", "account"),
            Rel("opportunity", "customerid", "account"),
            Rel("account", "createdby", "systemuser"));
        var contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname") }, new List<RelationshipInfo>());

        var pruned = PlanContextPruner.Prune(Request(account, more: contact));

        var kept = pruned[0].Relationships.Select(r => r.ReferencingEntity + "." + r.ReferencingAttribute).ToArray();
        Assert.Equal(new[] { "account.parentaccountid", "contact.parentcustomerid" }, kept);
    }

    /// <summary>
    /// Verifies that lookup attributes stay even when their relationship is dropped.
    /// </summary>
    [Fact]
    public void LookupAttributes_StayEvenWhenTheirRelationshipIsDropped()
    {
        var account = Account(new[] { A("accountid", "Uniqueidentifier"), A("name"), A("primarycontactid", "Lookup") }, Rel("account", "primarycontactid", "contact"));

        var pruned = PlanContextPruner.Prune(Request(account)).Single();

        Assert.Contains("primarycontactid", Names(pruned));
        Assert.Empty(pruned.Relationships);
    }

    /// <summary>
    /// Verifies that the pruned prompt is much smaller for aWide entity and the input is not changed.
    /// </summary>
    [Fact]
    public void ThePrunedPrompt_IsMuchSmaller_ForAWideEntity_AndTheInputIsNotChanged()
    {
        var attributes = new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name"), A("statecode", "State"), A("statecodename", "Virtual") };
        for (var i = 0; i < 40; i++) { attributes.Add(A($"address2_field{i}")); attributes.Add(A($"field{i}yominame")); attributes.Add(A($"money{i}", "Money")); attributes.Add(A($"money{i}_base", "Money")); }
        var entity = Account(attributes);
        var request = Request(entity);

        var pruned = PlanContextPruner.Prune(request).Single();

        Assert.Equal(3 + 40, pruned.Attributes.Count);
        Assert.Equal(4 + 160, attributes.Count);
        Assert.Equal(attributes.Count, request.Entities[0].Attributes.Count);
    }

    /// <summary>
    /// Verifies that an entity with nothing to drop comes back with the same content.
    /// </summary>
    [Fact]
    public void AnEntityWithNothingToDrop_ComesBackWithTheSameContent()
    {
        var entity = TestMetadata.Product();

        var pruned = PlanContextPruner.Prune(Request(entity)).Single();

        Assert.Equal(entity.Attributes.Select(a => a.LogicalName), pruned.Attributes.Select(a => a.LogicalName));
        Assert.Equal(entity.Relationships.Count, pruned.Relationships.Count);
        Assert.Equal(entity.Label, pruned.Label);
        Assert.Equal(entity.PrimaryNameAttribute, pruned.PrimaryNameAttribute);
    }
}
