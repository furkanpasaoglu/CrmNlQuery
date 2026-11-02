using System.Xml.Linq;
using CrmNlQuery.Tests.Contracts;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Tests the CRM user views store against a fake CRM server, passing the same contract as the in-memory fake.
/// </summary>
public class CrmUserViewsTests : UserViewStoreContract
{
    private readonly FakeCrmUserQueries _crm = new FakeCrmUserQueries();

    protected override IUserViewStore CreateStore()
    {
        return new CrmUserViews(new CrmClient(new HttpClient(_crm), new CrmOptions { BaseUrl = "http://crm/test" }));
    }

    protected override Task<IReadOnlyList<string>> IdsOfAsync(IUserViewStore store, Guid owner, string name)
    {
        IReadOnlyList<string> ids = _crm.Views.Where(v => v.Owner == owner && v.Name == name).OrderBy(v => v.CreatedOn).Select(v => v.Id.ToString("D")).ToList();
        return Task.FromResult(ids);
    }

    protected override Task<string> AddForeignSharedViewAsync(IUserViewStore store, Guid foreignOwner, Guid shareWith, string name)
        => Task.FromResult(_crm.Add(foreignOwner, name, "account", shared: true).Id.ToString("D"));

    protected override Task<IReadOnlyList<string>> ForeignIdsAsync(IUserViewStore store, Guid foreignOwner, string name)
        => IdsOfAsync(store, foreignOwner, name);

    protected override Task<string> AddDuplicateAsync(IUserViewStore store, Guid owner, string name)
        => Task.FromResult(_crm.Add(owner, name, "account").Id.ToString("D"));

    /// <summary>
    /// Verifies that everything runs as the caller with the owner filter in the query.
    /// </summary>
    [Fact]
    public async Task Everything_RunsAsTheCaller_WithTheOwnerFilterInTheQuery()
    {
        var store = CreateStore();
        var me = Guid.NewGuid();

        await store.UpsertAsync(me, Name, Accounts());

        var search = _crm.Requests.First(r => r.StartsWith("GET") && r.Contains("/userqueries?"));
        Assert.Contains("_ownerid_value eq " + me.ToString("D"), search);
        Assert.Contains("name eq 'NL Query - last result'", search);
    }

    /// <summary>
    /// Verifies that the created view carries the fetch and a layout with the entity's object type code.
    /// </summary>
    [Fact]
    public async Task TheCreatedView_CarriesTheFetchAndALayoutWithTheEntitysObjectTypeCode()
    {
        var store = CreateStore();
        var me = Guid.NewGuid();
        var view = new ViewDefinition("contract", "<fetch version=\"1.0\"><entity name=\"contract\"><attribute name=\"title\" /></entity></fetch>",
            new List<string> { "title", "cust.address1_city" }, "contractid", "title");

        await store.UpsertAsync(me, Name, view);

        var stored = Assert.Single(_crm.Views);
        Assert.Equal(view.FetchXml, stored.FetchXml);
        var grid = XDocument.Parse(stored.LayoutXml).Root!;
        Assert.Equal("1010", (string?)grid.Attribute("object"));
        Assert.Equal(new[] { "title", "cust.address1_city" }, grid.Element("row")!.Elements("cell").Select(c => (string?)c.Attribute("name")).ToArray());
    }

    /// <summary>
    /// Verifies that the object type code is read once and cached.
    /// </summary>
    [Fact]
    public async Task TheObjectTypeCode_IsReadOnce_AndCached()
    {
        var store = CreateStore();
        var me = Guid.NewGuid();

        await store.UpsertAsync(me, Name, Accounts());
        await store.UpsertAsync(me, Name, Accounts("name", "address1_city"));

        Assert.Equal(1, _crm.Requests.Count(r => r.Contains("EntityDefinitions")));
    }

    /// <summary>
    /// Verifies that a name with a quote is escaped in the filter.
    /// </summary>
    [Fact]
    public async Task ANameWithAQuote_IsEscapedInTheFilter()
    {
        var store = CreateStore();
        var me = Guid.NewGuid();

        await store.UpsertAsync(me, "O'Brien's view", Accounts());

        Assert.Contains(_crm.Requests, r => r.Contains("name eq 'O''Brien''s view'"));
        Assert.Single(_crm.Views, v => v.Name == "O'Brien's view");
    }

    /// <summary>
    /// Verifies that without the privilege a CRM exception with the permission flag is raised.
    /// </summary>
    [Fact]
    public async Task WithoutThePrivilege_ACrmExceptionWithThePermissionFlagIsRaised()
    {
        var store = CreateStore();
        _crm.FailNextWriteWith = "Principal user (Id=x, type=8) is missing prvCreateUserQuery privilege (Id=y)";

        var ex = await Assert.ThrowsAsync<CrmException>(() => store.UpsertAsync(Guid.NewGuid(), Name, Accounts()));

        Assert.True(ex.IsPermissionDenied);
        Assert.DoesNotContain("prvCreateUserQuery", ex.Message);
    }
}
