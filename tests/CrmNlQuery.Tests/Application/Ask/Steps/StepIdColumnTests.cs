using System.Text.Json;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;

namespace CrmNlQuery.Tests.Application.Ask.Steps;

/// <summary>
/// Holds tests for the id column of a producer step in dependent plans.
/// </summary>
public class StepIdColumnTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static RelationshipInfo Rel(string entity, string attribute, string target) => new RelationshipInfo(entity + "_" + attribute + "_" + target, entity, attribute, target, target + "id");

    private static FakeMetadataSource Metadata()
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String"), A("numberofemployees", "Integer") }, new List<RelationshipInfo>());
        var contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname", "String"), A("parentcustomerid", "Customer") },
            new List<RelationshipInfo> { Rel("contact", "parentcustomerid", "account"), Rel("contact", "parentcustomerid", "contact") });
        var opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A("customerid", "Customer"), A("ownerid", "Owner") },
            new List<RelationshipInfo> { Rel("opportunity", "customerid", "account"), Rel("opportunity", "customerid", "contact") });
        return new FakeMetadataSource(account, contact, opportunity);
    }

    private static StepIdColumn Column() => new StepIdColumn(Metadata());

    /// <summary>
    /// Verifies that aPlain query gets the root primary id as row id and keeps everything else.
    /// </summary>
    [Fact]
    public async Task APlainQuery_GetsTheRootPrimaryIdAsRowId_AndKeepsEverythingElse()
    {
        var xml = "<fetch top=\"1\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"numberofemployees\" descending=\"true\" /></entity></fetch>";

        var result = await Column().EnsureAsync(xml);

        var entity = XDocument.Parse(result).Root!.Element("entity")!;
        var rowId = Assert.Single(entity.Elements("attribute"), a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias);
        Assert.Equal("accountid", (string?)rowId.Attribute("name"));
        Assert.Equal("true", (string?)entity.Element("order")!.Attribute("descending"));
        Assert.Equal("1", (string?)XDocument.Parse(result).Root!.Attribute("top"));
    }

    /// <summary>
    /// Verifies that when the primary id was already selected it becomes the row id not aDuplicate.
    /// </summary>
    [Fact]
    public async Task WhenThePrimaryIdWasAlreadySelected_ItBecomesTheRowId_NotADuplicate()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"accountid\" /><attribute name=\"name\" /></entity></fetch>";

        var result = await Column().EnsureAsync(xml);

        var attributes = XDocument.Parse(result).Root!.Element("entity")!.Elements("attribute").ToList();
        Assert.Equal(2, attributes.Count);
        Assert.Equal(DistinctRewriter.RowIdAlias, (string?)attributes.Single(a => (string?)a.Attribute("name") == "accountid").Attribute("alias"));
    }

    /// <summary>
    /// Verifies that an existing row ID and aggregates are left untouched.
    /// </summary>
    [Fact]
    public async Task AnExistingRowId_AndAggregates_AreLeftUntouched()
    {
        var already = "<fetch><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"accountid\" alias=\"_rowid\" /></entity></fetch>";
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /><attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /></entity></fetch>";

        Assert.Equal(already, await Column().EnsureAsync(already));
        Assert.Equal(aggregate, await Column().EnsureAsync(aggregate));
    }

    /// <summary>
    /// Verifies that aBroken or unknown query is returned untouched.
    /// </summary>
    [Fact]
    public async Task ABrokenOrUnknownQuery_IsReturnedUntouched()
    {
        Assert.Equal("<fetch><entity", await Column().EnsureAsync("<fetch><entity"));
        var unknown = "<fetch><entity name=\"nope\"><attribute name=\"x\" /></entity></fetch>";
        Assert.Equal(unknown, await Column().EnsureAsync(unknown));
    }

    /// <summary>
    /// Verifies that for aPlain query the ids belong to the root entity.
    /// </summary>
    [Fact]
    public async Task ForAPlainQuery_TheIdsBelongToTheRootEntity()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"accountid\" alias=\"_rowid\" /></entity></fetch>";

        Assert.Equal(new[] { "account" }, await Column().IdEntitiesAsync(xml));
    }

    /// <summary>
    /// Verifies that for an aggregate grouped by aMulti target lookup all the targets are listed.
    /// </summary>
    [Fact]
    public async Task ForAnAggregateGroupedByAMultiTargetLookup_AllTheTargetsAreListed()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /><attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /></entity></fetch>";

        var entities = await Column().IdEntitiesAsync(xml);

        Assert.Equal(new[] { "account", "contact" }, entities.OrderBy(e => e, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Verifies that for an aggregate grouped by aPrimary id the ids belong to that entity.
    /// </summary>
    [Fact]
    public async Task ForAnAggregateGroupedByAPrimaryId_TheIdsBelongToThatEntity()
    {
        var xml = "<fetch aggregate=\"true\"><entity name=\"contact\"><attribute name=\"contactid\" aggregate=\"count\" alias=\"n\" />" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"parentcustomerid\" alias=\"a\"><attribute name=\"accountid\" groupby=\"true\" alias=\"_rowid\" /></link-entity></entity></fetch>";

        Assert.Equal(new[] { "account" }, await Column().IdEntitiesAsync(xml));
    }

    /// <summary>
    /// Verifies that when the id entities cannot be determined the result is empty not aGuess.
    /// </summary>
    [Fact]
    public async Task WhenTheIdEntitiesCannotBeDetermined_TheResultIsEmpty_NotAGuess()
    {
        var ownerGroup = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /><attribute name=\"ownerid\" groupby=\"true\" alias=\"_rowid\" /></entity></fetch>";
        var noRowId = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /></entity></fetch>";

        Assert.Empty(await Column().IdEntitiesAsync(ownerGroup));
        Assert.Empty(await Column().IdEntitiesAsync(noRowId));
    }

    /// <summary>
    /// Verifies that aRow id inside aLink entity is read as the row id not as aColumn.
    /// </summary>
    [Fact]
    public void ARowIdInsideALinkEntity_IsReadAsTheRowId_NotAsAColumn()
    {
        var fetch = "<fetch aggregate=\"true\"><entity name=\"contact\"><attribute name=\"contactid\" aggregate=\"count\" alias=\"n\" />" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"parentcustomerid\" alias=\"a\"><attribute name=\"accountid\" groupby=\"true\" alias=\"_rowid\" /></link-entity></entity></fetch>";
        var rows = JsonDocument.Parse("{\"value\":[{\"n\":2,\"_rowid\":\"37870e9c-c7c7-f111-b4df-00155d01f303\"}]}").RootElement.GetProperty("value").Clone();

        var table = ResultTableBuilder.Build(rows, fetch);

        Assert.Equal(new[] { "n" }, table.Columns);
        Assert.Equal(new[] { "37870e9c-c7c7-f111-b4df-00155d01f303" }, table.RowIds);
    }
}
