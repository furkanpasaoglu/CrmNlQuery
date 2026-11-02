using System.Text.Json;
using CrmNlQuery.Tests.Contracts;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Live tests for crm user views against real external systems.
/// </summary>
[Trait("Category", "Live")]
public class CrmUserViewsLiveTests : UserViewStoreContract, IAsyncLifetime
{
    private const string OtherName = "My own saved view";

    private static readonly CrmOptions Options = LiveOptions();
    private static readonly CrmClient Client = CrmClient.Create(Options);
    private static readonly Lazy<(Guid A, Guid B)> Users = new Lazy<(Guid, Guid)>(() => (Lookup("testuser_a"), Lookup("testuser_b")));
    private int _next;

    private static CrmOptions LiveOptions()
    {
        var options = LiveMetadataTests.LiveOptions();
        options.MetadataCacheDir = Path.Combine(Path.GetTempPath(), "crmnl-liveviews-" + Guid.NewGuid().ToString("N"));
        return options;
    }

    private static Guid Lookup(string account)
    {
        var filter = Uri.EscapeDataString($"domainname eq '{Options.Domain}\\{account}'");
        using var doc = Client.GetAsServiceAsync("systemusers?$select=systemuserid&$filter=" + filter).GetAwaiter().GetResult();
        return Guid.Parse(doc.RootElement.GetProperty("value")[0].GetProperty("systemuserid").GetString()!);
    }

    protected override Guid NewUser() => _next++ == 0 ? Users.Value.A : Users.Value.B;

    protected override IUserViewStore CreateStore() => new CrmUserViews(Client);

    private static async Task<IReadOnlyList<string>> OwnedIdsAsync(Guid owner, string name)
    {
        var filter = Uri.EscapeDataString($"name eq '{name}' and _ownerid_value eq {owner:D}");
        using var doc = await Client.GetAsync("userqueries?$select=userqueryid&$orderby=createdon asc&$filter=" + filter, Caller.User(owner));
        return doc.RootElement.GetProperty("value").EnumerateArray().Select(v => v.GetProperty("userqueryid").GetString()!).ToList();
    }

    protected override Task<IReadOnlyList<string>> IdsOfAsync(IUserViewStore store, Guid owner, string name) => OwnedIdsAsync(owner, name);

    protected override Task<IReadOnlyList<string>> ForeignIdsAsync(IUserViewStore store, Guid foreignOwner, string name) => OwnedIdsAsync(foreignOwner, name);

    private static async Task<string> CreateRawAsync(Guid owner, string name)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = name,
            ["returnedtypecode"] = "account",
            ["querytype"] = 0,
            ["fetchxml"] = "<fetch version=\"1.0\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>",
            ["layoutxml"] = "<grid name=\"resultset\" object=\"1\" jump=\"name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"accountid\"><cell name=\"name\" width=\"150\" /></row></grid>",
        });
        using var created = await Client.SendJsonAsync(HttpMethod.Post, "userqueries", body, owner);
        return created!.RootElement.GetProperty("userqueryid").GetString()!;
    }

    protected override async Task<string> AddForeignSharedViewAsync(IUserViewStore store, Guid foreignOwner, Guid shareWith, string name)
    {
        var id = await CreateRawAsync(foreignOwner, name);
        var grant = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Target"] = new Dictionary<string, string> { ["@odata.type"] = "Microsoft.Dynamics.CRM.userquery", ["userqueryid"] = id },
            ["PrincipalAccess"] = new Dictionary<string, object>
            {
                ["Principal"] = new Dictionary<string, string> { ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser", ["systemuserid"] = shareWith.ToString("D") },
                ["AccessMask"] = "ReadAccess",
            },
        });
        (await Client.SendJsonAsync(HttpMethod.Post, "GrantAccess", grant, foreignOwner))?.Dispose();
        return id;
    }

    protected override Task<string> AddDuplicateAsync(IUserViewStore store, Guid owner, string name) => CreateRawAsync(owner, name);

    /// <summary>
    /// Prepares the live CRM user-view tests.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Deletes the test views both users created after each live test so nothing leaks into the CRM.
    /// </summary>
    public async Task DisposeAsync()
    {
        var store = CreateStore();
        foreach (var user in new[] { Users.Value.A, Users.Value.B })
        {
            await store.DeleteAsync(user, Name);
            await store.DeleteAsync(user, OtherName);
        }
    }

    /// <summary>
    /// Verifies that the shared view of another user is really visible to the caller but never touched.
    /// </summary>
    [Fact]
    public async Task TheSharedViewOfAnotherUser_IsReallyVisibleToTheCaller_ButNeverTouched()
    {
        var store = CreateStore();
        var me = NewUser();
        var stranger = NewUser();
        var foreign = await AddForeignSharedViewAsync(store, stranger, me, Name);

        var filter = Uri.EscapeDataString($"name eq '{Name}'");
        using var byNameOnly = await Client.GetAsync("userqueries?$select=userqueryid,_ownerid_value&$filter=" + filter, Caller.User(me));
        var visible = byNameOnly.RootElement.GetProperty("value").EnumerateArray().Select(v => v.GetProperty("userqueryid").GetString()).ToList();
        Assert.Contains(foreign, visible);

        var mine = await store.UpsertAsync(me, Name, Accounts());
        Assert.NotEqual(foreign, mine);
        Assert.Contains(foreign, await OwnedIdsAsync(stranger, Name));
    }

    /// <summary>
    /// Verifies that the view is owned by the caller not by the service account.
    /// </summary>
    [Fact]
    public async Task TheViewIsOwnedByTheCaller_NotByTheServiceAccount()
    {
        var store = CreateStore();
        var me = NewUser();

        var id = await store.UpsertAsync(me, Name, Accounts());

        using var doc = await Client.GetAsync($"userqueries({id})?$select=_ownerid_value,_createdby_value", Caller.User(me));
        Assert.Equal(me, Guid.Parse(doc.RootElement.GetProperty("_ownerid_value").GetString()!));
        Assert.Equal(me, Guid.Parse(doc.RootElement.GetProperty("_createdby_value").GetString()!));
    }
}
