using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests that the distinct rewriter adds distinct and a hidden id only when a one-to-many link is used as a filter.
/// </summary>
public class DistinctRewriterTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static FakeMetadataSource Metadata()
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String"), A("address1_city", "String"), A("primarycontactid", "Lookup") },
            new List<RelationshipInfo>());
        var opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A("customerid", "Customer"), A("statecode", "State") },
            new List<RelationshipInfo>());
        var contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname", "String") },
            new List<RelationshipInfo>());
        return new FakeMetadataSource(account, opportunity, contact);
    }

    private static Task<string> Apply(string fetchXml) => new DistinctRewriter(Metadata()).ApplyAsync(fetchXml);

    private const string OneToManyFilterLink =
        "<link-entity name=\"opportunity\" from=\"customerid\" to=\"accountid\" alias=\"o\" link-type=\"inner\">" +
        "<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></link-entity>";

    private static string Account(string body, string fetchAttributes = "") =>
        $"<fetch top=\"500\"{fetchAttributes}><entity name=\"account\">{body}</entity></fetch>";

    private static XElement Entity(string xml) => XDocument.Parse(xml).Root!.Element("entity")!;

    /// <summary>
    /// Verifies that a one-to-many filter link adds distinct and a hidden row id.
    /// </summary>
    [Fact]
    public async Task OneToManyFilterLink_AddsDistinct_AndAHiddenRowId()
    {
        var result = await Apply(Account("<attribute name=\"name\" />" + OneToManyFilterLink));

        var root = XDocument.Parse(result).Root!;
        Assert.Equal("true", (string?)root.Attribute("distinct"));
        var rowId = Assert.Single(root.Element("entity")!.Elements("attribute"), a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias);
        Assert.Equal("accountid", (string?)rowId.Attribute("name"));
    }

    /// <summary>
    /// Verifies that nothing is added for the id when the user already selected it.
    /// </summary>
    [Fact]
    public async Task WhenTheUserAlreadySelectedTheId_NothingIsAddedForIt()
    {
        var result = await Apply(Account("<attribute name=\"accountid\" /><attribute name=\"name\" />" + OneToManyFilterLink));

        Assert.Equal("true", (string?)XDocument.Parse(result).Root!.Attribute("distinct"));
        Assert.DoesNotContain(Entity(result).Elements("attribute"), a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias);
        Assert.Equal(2, Entity(result).Elements("attribute").Count());
    }

    /// <summary>
    /// Verifies that order, filter and top are kept exactly.
    /// </summary>
    [Fact]
    public async Task OrderAndFilterAndTop_AreKeptExactly()
    {
        var body = "<attribute name=\"name\" /><order attribute=\"name\" descending=\"true\" /><order attribute=\"address1_city\" descending=\"false\" />" +
                   "<filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter>" + OneToManyFilterLink;
        var before = Entity(Account(body));

        var result = await Apply(Account(body));
        var after = Entity(result);

        Assert.Equal(before.Elements("order").Select(o => o.ToString()), after.Elements("order").Select(o => o.ToString()));
        Assert.Equal(before.Element("filter")!.ToString(), after.Element("filter")!.ToString());
        Assert.Equal(before.Element("link-entity")!.ToString(), after.Element("link-entity")!.ToString());
        Assert.Equal("500", (string?)XDocument.Parse(result).Root!.Attribute("top"));
    }

    /// <summary>
    /// Verifies that rows of the related table are expected duplicates so nothing changes.
    /// </summary>
    [Fact]
    public async Task RowsOfTheRelatedTable_AreExpectedDuplicates_SoNothingChanges()
    {
        var xml = Account("<attribute name=\"name\" /><link-entity name=\"opportunity\" from=\"customerid\" to=\"accountid\" alias=\"o\"><attribute name=\"name\" /></link-entity>");

        Assert.Equal(xml, await Apply(xml));
    }

    /// <summary>
    /// Verifies that a mixed query with a separate link with columns is left alone.
    /// </summary>
    [Fact]
    public async Task MixedQuery_ASeparateLinkWithColumns_IsLeftAlone()
    {
        var xml = Account("<attribute name=\"name\" />" + OneToManyFilterLink +
            "<link-entity name=\"contact\" from=\"contactid\" to=\"primarycontactid\" alias=\"c\"><attribute name=\"fullname\" /></link-entity>");

        Assert.Equal(xml, await Apply(xml));
    }

    /// <summary>
    /// Verifies that a many-to-one link does not multiply rows so nothing changes.
    /// </summary>
    [Fact]
    public async Task ManyToOneLink_DoesNotMultiplyRows_SoNothingChanges()
    {
        var xml = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" />" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"a\"><filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter></link-entity></entity></fetch>";

        Assert.Equal(xml, await Apply(xml));
    }

    /// <summary>
    /// Verifies that a query with no link or an aggregate is left alone.
    /// </summary>
    [Fact]
    public async Task NoLink_OrAggregate_IsLeftAlone()
    {
        var plain = Account("<attribute name=\"name\" />");
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" aggregate=\"count\" alias=\"n\" />" + OneToManyFilterLink + "</entity></fetch>";

        Assert.Equal(plain, await Apply(plain));
        Assert.Equal(aggregate, await Apply(aggregate));
    }

    /// <summary>
    /// Verifies that applying the rewrite twice changes nothing more.
    /// </summary>
    [Fact]
    public async Task ApplyingTwice_ChangesNothingMore()
    {
        var once = await Apply(Account("<attribute name=\"name\" />" + OneToManyFilterLink));

        Assert.Equal(once, await Apply(once));
    }

    /// <summary>
    /// Verifies that an already distinct query still gets the row ID but is otherwise the same.
    /// </summary>
    [Fact]
    public async Task AnAlreadyDistinctQuery_StillGetsTheRowId_ButIsOtherwiseTheSame()
    {
        var result = await Apply(Account("<attribute name=\"name\" />" + OneToManyFilterLink, " distinct=\"true\""));

        Assert.Equal("true", (string?)XDocument.Parse(result).Root!.Attribute("distinct"));
        Assert.Single(Entity(result).Elements("attribute"), a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias);
    }

    /// <summary>
    /// Verifies that a broken or unknown query is returned untouched.
    /// </summary>
    [Fact]
    public async Task ABrokenOrUnknownQuery_IsReturnedUntouched()
    {
        Assert.Equal("<fetch><entity", await Apply("<fetch><entity"));
        var unknown = "<fetch><entity name=\"nope\"><attribute name=\"x\" /></entity></fetch>";
        Assert.Equal(unknown, await Apply(unknown));
    }
}
