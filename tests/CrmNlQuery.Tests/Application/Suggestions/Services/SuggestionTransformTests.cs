using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Suggestions.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Suggestions.Services;

/// <summary>
/// Tests the suggestion transforms that turn a stored query into a narrowed, grouped, sorted or time-filtered query.
/// </summary>
public class SuggestionTransformTests
{
    private static AttributeInfo A(string name, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, name == "statecode" ? "Status" : name, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static EntityInfo Opportunity() => new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Uniqueidentifier"), A("name", "String"),
            A("statecode", "State", (0, "Open"), (1, "Won"), (2, "Lost")),
            A("prioritycode", "Picklist", (1, "High"), (2, "Normal"), (3, "Low")),
            A("ownerid", "Owner"), A("estimatedvalue", "Money"), A("createdon", "DateTime"),
        },
        new List<RelationshipInfo>());

    private const string Plain = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><order attribute=\"name\" /><filter><condition attribute=\"name\" operator=\"like\" value=\"%a%\" /></filter></entity></fetch>";

    private static async Task<ValidationResult> Validate(XDocument doc)
        => await new PlanValidator(new FakeMetadataSource(Opportunity())).ValidateFetchXmlAsync(doc.ToString(SaveOptions.DisableFormatting));

    /// <summary>
    /// Verifies that narrow adds an equality condition to the existing filter.
    /// </summary>
    [Fact]
    public async Task Narrow_AddsAnEqualityCondition_ToTheExistingFilter()
    {
        var applied = SuggestionTransform.Apply(Plain, Opportunity(), "narrow:statecode:0")!;

        Assert.Equal("Status: Open", applied.Label);
        var filter = applied.Document.Root!.Element("entity")!.Element("filter")!;
        Assert.Equal(2, filter.Elements("condition").Count());
        Assert.Contains(filter.Elements("condition"), c => (string?)c.Attribute("attribute") == "statecode" && (string?)c.Attribute("value") == "0");
        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that narrow on a query without a filter creates one.
    /// </summary>
    [Fact]
    public async Task Narrow_OnAQueryWithoutAFilter_CreatesOne()
    {
        var fetch = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>";

        var applied = SuggestionTransform.Apply(fetch, Opportunity(), "narrow:prioritycode:1")!;

        Assert.True((await Validate(applied.Document)).IsValid);
        Assert.Single(applied.Document.Root!.Descendants("condition"));
    }

    /// <summary>
    /// Verifies that narrow on an or filter wraps it so the or stays inside an and.
    /// </summary>
    [Fact]
    public async Task Narrow_OnAnOrFilter_WrapsItSoTheOrStaysInsideAnAnd()
    {
        var fetch = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><filter type=\"or\"><condition attribute=\"name\" operator=\"like\" value=\"%a%\" /><condition attribute=\"name\" operator=\"like\" value=\"%b%\" /></filter></entity></fetch>";

        var applied = SuggestionTransform.Apply(fetch, Opportunity(), "narrow:statecode:1")!;

        var outer = applied.Document.Root!.Element("entity")!.Elements("filter").Single();
        Assert.Equal("and", (string?)outer.Attribute("type"));
        Assert.Equal("or", (string?)outer.Element("filter")!.Attribute("type"));
        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that invalid or unknown narrow suggestion ids are rejected.
    /// </summary>
    [Theory]
    [InlineData("narrow:statecode:9")]
    [InlineData("narrow:statecode:abc")]
    [InlineData("narrow:nosuchfield:1")]
    [InlineData("narrow:name:1")]
    [InlineData("group:name")]
    [InlineData("sort:statecode")]
    [InlineData("time:createdon:365d")]
    [InlineData("related:incident")]
    [InlineData("whatever")]
    [InlineData("")]
    public void InvalidSuggestions_AreRejected(string id)
    {
        Assert.Null(SuggestionTransform.Apply(Plain, Opportunity(), id));
    }

    /// <summary>
    /// Verifies that an already filtered field is rejected.
    /// </summary>
    [Fact]
    public void AnAlreadyFilteredField_IsRejected()
    {
        var fetch = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></entity></fetch>";

        Assert.Null(SuggestionTransform.Apply(fetch, Opportunity(), "narrow:statecode:1"));
    }

    /// <summary>
    /// Verifies that Group turns the list into a count by that field while keeping the filters.
    /// </summary>
    [Fact]
    public async Task Group_TurnsTheListIntoACountByThatField_KeepingTheFilters()
    {
        var applied = SuggestionTransform.Apply(Plain, Opportunity(), "group:prioritycode")!;

        var root = applied.Document.Root!;
        Assert.Equal("true", (string?)root.Attribute("aggregate"));
        Assert.Null(root.Attribute("top"));
        var attributes = root.Element("entity")!.Elements("attribute").ToList();
        Assert.Equal(2, attributes.Count);
        Assert.Equal("true", (string?)attributes[0].Attribute("groupby"));
        Assert.Equal("countcolumn", (string?)attributes[1].Attribute("aggregate"));
        Assert.Equal("n", (string?)root.Element("entity")!.Element("order")!.Attribute("alias"));
        Assert.NotNull(root.Element("entity")!.Element("filter"));
        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that grouping by a lookup is allowed.
    /// </summary>
    [Fact]
    public async Task Group_ByALookup_IsAllowed()
    {
        var applied = SuggestionTransform.Apply(Plain, Opportunity(), "group:ownerid")!;

        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that group removes distinct and link columns but keeps the link.
    /// </summary>
    [Fact]
    public async Task Group_RemovesDistinct_AndLinkColumns_ButKeepsTheLink()
    {
        var fetch = "<fetch distinct=\"true\" top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" />" +
                    "<link-entity name=\"opportunity\" from=\"opportunityid\" to=\"opportunityid\" alias=\"l\"><attribute name=\"name\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></link-entity></entity></fetch>";

        var applied = SuggestionTransform.Apply(fetch, Opportunity(), "group:prioritycode")!;

        var root = applied.Document.Root!;
        Assert.Null(root.Attribute("distinct"));
        Assert.Single(root.Descendants("link-entity"));
        Assert.Empty(root.Descendants("link-entity").Single().Elements("attribute"));
        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that sort orders descending with top ten and shows the column.
    /// </summary>
    [Fact]
    public async Task Sort_OrdersDescending_WithTopTen_AndShowsTheColumn()
    {
        var applied = SuggestionTransform.Apply(Plain, Opportunity(), "sort:estimatedvalue")!;

        var root = applied.Document.Root!;
        Assert.Equal("10", (string?)root.Attribute("top"));
        var orders = root.Element("entity")!.Elements("order").ToList();
        Assert.Single(orders);
        Assert.Equal("estimatedvalue", (string?)orders[0].Attribute("attribute"));
        Assert.Equal("true", (string?)orders[0].Attribute("descending"));
        Assert.Contains(root.Element("entity")!.Elements("attribute"), a => (string?)a.Attribute("name") == "estimatedvalue");
        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that Time adds a last-thirty-days condition on createdon.
    /// </summary>
    [Fact]
    public async Task Time_AddsALastThirtyDaysCondition_OnCreatedOn()
    {
        var applied = SuggestionTransform.Apply(Plain, Opportunity(), "time:createdon:30d")!;

        Assert.Contains(applied.Document.Root!.Descendants("condition"), c => (string?)c.Attribute("attribute") == "createdon" && (string?)c.Attribute("operator") == "last-x-days" && (string?)c.Attribute("value") == "30");
        Assert.True((await Validate(applied.Document)).IsValid);
    }

    /// <summary>
    /// Verifies that an aggregate query is not changed.
    /// </summary>
    [Fact]
    public void AnAggregateQuery_IsNotChanged()
    {
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"statecode\" groupby=\"true\" alias=\"s\" /></entity></fetch>";

        Assert.Null(SuggestionTransform.Apply(aggregate, Opportunity(), "narrow:prioritycode:1"));
    }
}
