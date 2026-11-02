using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Application.CrmViews.Services;

namespace CrmNlQuery.Tests.Application.CrmViews.Services;

/// <summary>
/// Tests building a CRM view from a stored FetchXML, validating the original query before removing paging attributes.
/// </summary>
public class ViewBuilderTests
{
    private static ViewBuilder Builder()
    {
        var metadata = TestMetadata.Create();
        return new ViewBuilder(new PlanValidator(metadata), metadata);
    }

    private const string Accounts =
        "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"address1_city\" />" +
        "<order attribute=\"name\" descending=\"true\" /><order attribute=\"address1_city\" />" +
        "<filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter></entity></fetch>";

    private static XElement Root(string fetchXml) => XDocument.Parse(fetchXml).Root!;

    /// <summary>
    /// Verifies that the row cap and other paging attributes are removed.
    /// </summary>
    [Fact]
    public async Task TheRowCap_AndOtherPagingAttributes_AreRemoved()
    {
        var result = await Builder().BuildAsync(Accounts);

        Assert.True(result.Ok);
        var fetch = Root(result.View!.FetchXml);
        Assert.Null(fetch.Attribute("top"));
        Assert.Null(fetch.Attribute("count"));
        Assert.Null(fetch.Attribute("page"));
        Assert.Null(fetch.Attribute("paging-cookie"));
    }

    /// <summary>
    /// Verifies that ordering and filters survive in the same order.
    /// </summary>
    [Fact]
    public async Task OrderingAndFilters_SurviveInTheSameOrder()
    {
        var result = await Builder().BuildAsync(Accounts);

        var entity = Root(result.View!.FetchXml).Element("entity")!;
        var orders = entity.Elements("order").ToList();
        Assert.Equal(new[] { "name", "address1_city" }, orders.Select(o => (string?)o.Attribute("attribute")).ToArray());
        Assert.Equal("true", (string?)orders[0].Attribute("descending"));
        var condition = entity.Element("filter")!.Element("condition")!;
        Assert.Equal("address1_city", (string?)condition.Attribute("attribute"));
        Assert.Equal("London", (string?)condition.Attribute("value"));
        Assert.Equal(new[] { "name", "address1_city" }, result.View.Columns.ToArray());
        Assert.Equal("account", result.View.Entity);
        Assert.Equal("accountid", result.View.PrimaryIdAttribute);
        Assert.Equal("name", result.View.PrimaryNameAttribute);
    }

    /// <summary>
    /// Verifies that every order gets an explicit descending attribute so the CRM grid keeps our direction.
    /// </summary>
    [Fact]
    public async Task EveryOrder_GetsAnExplicitDescendingAttribute_SoTheCrmGridKeepsOurDirection()
    {
        var implicitOrder = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"name\" /><order attribute=\"address1_city\" descending=\"true\" /></entity></fetch>";

        var result = await Builder().BuildAsync(implicitOrder);

        var orders = Root(result.View!.FetchXml).Element("entity")!.Elements("order").ToList();
        Assert.Equal(new[] { "false", "true" }, orders.Select(o => (string?)o.Attribute("descending")).ToArray());
    }

    /// <summary>
    /// Verifies that orders inside a link are made explicit too.
    /// </summary>
    [Fact]
    public async Task OrdersInsideALink_AreMadeExplicitToo()
    {
        var linked = "<fetch top=\"500\"><entity name=\"contract\"><attribute name=\"title\" />" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"cust\"><attribute name=\"name\" /><order attribute=\"name\" /></link-entity></entity></fetch>";

        var result = await Builder().BuildAsync(linked);

        Assert.True(result.Ok);
        var order = Root(result.View!.FetchXml).Descendants("link-entity").Single().Element("order")!;
        Assert.Equal("false", (string?)order.Attribute("descending"));
    }

    /// <summary>
    /// Verifies that a query without any order stays without one.
    /// </summary>
    [Fact]
    public async Task AQueryWithoutAnyOrder_StaysWithoutOne()
    {
        var result = await Builder().BuildAsync("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>");

        Assert.Empty(Root(result.View!.FetchXml).Descendants("order"));
    }

    /// <summary>
    /// Verifies that a count or paging cookie on the stored fetch never reaches the view when the validator allows it not.
    /// </summary>
    [Fact]
    public async Task ACountOrPagingCookieOnTheStoredFetch_NeverReachesTheView_WhenTheValidatorAllowsItNot()
    {
        var withCount = "<fetch top=\"500\" count=\"50\" page=\"2\" paging-cookie=\"x\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>";

        var result = await Builder().BuildAsync(withCount);

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.Invalid, result.Rejection);
    }

    /// <summary>
    /// Verifies that a query the validator rejects never becomes a view.
    /// </summary>
    [Theory]
    [InlineData("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"nosuchcolumn\" /></entity></fetch>")]
    [InlineData("<fetch top=\"500\"><entity name=\"nosuchentity\"><attribute name=\"name\" /></entity></fetch>")]
    [InlineData("<fetch top=\"500\"><entity name=\"account\"><all-attributes /></entity></fetch>")]
    [InlineData("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><delete /></entity></fetch>")]
    public async Task AQueryTheValidatorRejects_NeverBecomesAView(string stored)
    {
        var result = await Builder().BuildAsync(stored);

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.Invalid, result.Rejection);
    }

    /// <summary>
    /// Verifies that broken XML is rejected without throwing.
    /// </summary>
    [Fact]
    public async Task BrokenXml_IsRejectedWithoutThrowing()
    {
        var result = await Builder().BuildAsync("<fetch><entity");

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.Unparseable, result.Rejection);
    }

    /// <summary>
    /// Verifies that the validator runs on the original query so its row cap rule still applies.
    /// </summary>
    [Fact]
    public async Task TheValidatorRunsOnTheOriginalQuery_SoItsRowCapRuleStillApplies()
    {
        var big = "<fetch top=\"100000\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>";

        var result = await Builder().BuildAsync(big);

        Assert.True(result.Ok);
        Assert.Null(Root(result.View!.FetchXml).Attribute("top"));
    }

    /// <summary>
    /// Verifies that an aggregate query can not be a view.
    /// </summary>
    [Fact]
    public async Task AnAggregateQuery_CanNotBeAView()
    {
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"n\" aggregate=\"count\" /></entity></fetch>";

        var result = await Builder().BuildAsync(aggregate);

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.Aggregate, result.Rejection);
    }

    /// <summary>
    /// Verifies that a no column query is rejected.
    /// </summary>
    [Fact]
    public async Task ANoColumnQuery_IsRejected()
    {
        var result = await Builder().BuildAsync("<fetch top=\"500\"><entity name=\"account\"><filter><condition attribute=\"name\" operator=\"eq\" value=\"x\" /></filter></entity></fetch>");

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.NoColumns, result.Rejection);
    }

    private const string ContractWithCustomerCity =
        "<fetch top=\"500\"><entity name=\"contract\"><attribute name=\"title\" />" +
        "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"cust\"><attribute name=\"address1_city\" /></link-entity></entity></fetch>";

    /// <summary>
    /// Verifies that a many to one column is allowed as an aliased cell.
    /// </summary>
    [Fact]
    public async Task AManyToOneColumn_IsAllowed_AsAnAliasedCell()
    {
        var result = await Builder().BuildAsync(ContractWithCustomerCity);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "title", "cust.address1_city" }, result.View!.Columns.ToArray());
        Assert.Equal("contractid", result.View.PrimaryIdAttribute);
    }

    /// <summary>
    /// Verifies that a link without an alias is an invalid query.
    /// </summary>
    [Fact]
    public async Task ALinkWithoutAnAlias_IsAnInvalidQuery()
    {
        var noAlias = ContractWithCustomerCity.Replace(" alias=\"cust\"", "");

        var result = await Builder().BuildAsync(noAlias);

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.Invalid, result.Rejection);
    }

    /// <summary>
    /// Verifies that a one to many column is rejected.
    /// </summary>
    [Fact]
    public async Task AOneToManyColumn_IsRejected()
    {
        var oneToMany = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" />" +
            "<link-entity name=\"contract\" from=\"customerid\" to=\"accountid\" alias=\"c\"><attribute name=\"title\" /></link-entity></entity></fetch>";

        var result = await Builder().BuildAsync(oneToMany);

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.OneToManyColumns, result.Rejection);
    }

    /// <summary>
    /// Verifies that a link used only as a filter is fine in either direction.
    /// </summary>
    [Fact]
    public async Task ALinkUsedOnlyAsAFilter_IsFine_InEitherDirection()
    {
        var filterOnly = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" />" +
            "<link-entity name=\"contract\" from=\"customerid\" to=\"accountid\" alias=\"c\" link-type=\"inner\" /></entity></fetch>";

        var result = await Builder().BuildAsync(filterOnly);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "name" }, result.View!.Columns.ToArray());
        Assert.Single(Root(result.View.FetchXml).Descendants("link-entity"));
    }

    /// <summary>
    /// Verifies that columns from a nested link are rejected.
    /// </summary>
    [Fact]
    public async Task ColumnsFromANestedLink_AreRejected()
    {
        var nested = "<fetch top=\"500\"><entity name=\"contractdetail\"><attribute name=\"title\" />" +
            "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"k\">" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"a\"><attribute name=\"name\" /></link-entity></link-entity></entity></fetch>";

        var result = await Builder().BuildAsync(nested);

        Assert.False(result.Ok);
        Assert.Equal(ViewRejection.NestedColumns, result.Rejection);
    }

    /// <summary>
    /// Verifies that the layout has one cell per column in order with the ID and jump from metadata.
    /// </summary>
    [Fact]
    public async Task TheLayout_HasOneCellPerColumn_InOrder_WithTheIdAndJumpFromMetadata()
    {
        var result = await Builder().BuildAsync(ContractWithCustomerCity);

        var grid = XDocument.Parse(ViewLayout.ToLayoutXml(result.View!, 1010)).Root!;

        Assert.Equal("1010", (string?)grid.Attribute("object"));
        Assert.Equal("title", (string?)grid.Attribute("jump"));
        var row = grid.Element("row")!;
        Assert.Equal("contractid", (string?)row.Attribute("id"));
        Assert.Equal(new[] { "title", "cust.address1_city" }, row.Elements("cell").Select(c => (string?)c.Attribute("name")).ToArray());
        Assert.All(row.Elements("cell"), c => Assert.Equal("150", (string?)c.Attribute("width")));
    }
}
