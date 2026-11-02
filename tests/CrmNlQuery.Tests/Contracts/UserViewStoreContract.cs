using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;

namespace CrmNlQuery.Tests.Contracts;

/// <summary>
/// Shared behaviours every user view store implementation must pass, touching only the caller's own exactly named view.
/// </summary>
public abstract class UserViewStoreContract
{
    protected const string Name = "NL Query - last result";

    protected abstract IUserViewStore CreateStore();

    protected virtual Guid NewUser() => Guid.NewGuid();

    protected abstract Task<IReadOnlyList<string>> IdsOfAsync(IUserViewStore store, Guid owner, string name);
    protected abstract Task<string> AddForeignSharedViewAsync(IUserViewStore store, Guid foreignOwner, Guid shareWith, string name);
    protected abstract Task<IReadOnlyList<string>> ForeignIdsAsync(IUserViewStore store, Guid foreignOwner, string name);
    protected abstract Task<string> AddDuplicateAsync(IUserViewStore store, Guid owner, string name);

    protected static ViewDefinition Accounts(params string[] columns)
        => new ViewDefinition("account", "<fetch version=\"1.0\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>",
            columns.Length == 0 ? new List<string> { "name" } : columns.ToList(), "accountid", "name");

    protected static ViewDefinition Products()
        => new ViewDefinition("product", "<fetch version=\"1.0\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>",
            new List<string> { "name" }, "productid", "name");

    /// <summary>
    /// Verifies that the first upsert creates one view owned by the caller.
    /// </summary>
    [Fact]
    public async Task TheFirstUpsert_CreatesOneView_OwnedByTheCaller()
    {
        var store = CreateStore();
        var me = NewUser();

        var id = await store.UpsertAsync(me, Name, Accounts());

        Assert.Equal(new[] { id }, (await IdsOfAsync(store, me, Name)).ToArray());
        await store.DeleteAsync(me, Name);
    }

    /// <summary>
    /// Verifies that another upsert updates the same view and never adds a second.
    /// </summary>
    [Fact]
    public async Task AnotherUpsert_UpdatesTheSameView_NeverAddsASecond()
    {
        var store = CreateStore();
        var me = NewUser();

        var first = await store.UpsertAsync(me, Name, Accounts());
        var second = await store.UpsertAsync(me, Name, Accounts("name", "address1_city"));

        Assert.Equal(first, second);
        Assert.Single(await IdsOfAsync(store, me, Name));
        await store.DeleteAsync(me, Name);
    }

    /// <summary>
    /// Verifies that a changed entity type replaces the view.
    /// </summary>
    [Fact]
    public async Task AChangedEntityType_ReplacesTheView()
    {
        var store = CreateStore();
        var me = NewUser();
        var first = await store.UpsertAsync(me, Name, Accounts());

        var second = await store.UpsertAsync(me, Name, Products());

        Assert.NotEqual(first, second);
        Assert.Equal(new[] { second }, (await IdsOfAsync(store, me, Name)).ToArray());
        await store.DeleteAsync(me, Name);
    }

    /// <summary>
    /// Verifies that concurrent duplicates are collapsed keeping the first.
    /// </summary>
    [Fact]
    public async Task ConcurrentDuplicates_AreCollapsed_KeepingTheFirst()
    {
        var store = CreateStore();
        var me = NewUser();
        var first = await store.UpsertAsync(me, Name, Accounts());
        await AddDuplicateAsync(store, me, Name);
        await AddDuplicateAsync(store, me, Name);
        Assert.Equal(3, (await IdsOfAsync(store, me, Name)).Count);

        var used = await store.UpsertAsync(me, Name, Accounts("name", "address1_city"));

        Assert.Equal(first, used);
        Assert.Equal(new[] { first }, (await IdsOfAsync(store, me, Name)).ToArray());
        await store.DeleteAsync(me, Name);
    }

    /// <summary>
    /// Verifies that a shared view of someone else with the same name is never updated or deleted.
    /// </summary>
    [Fact]
    public async Task ASharedViewOfSomeoneElseWithTheSameName_IsNeverUpdatedOrDeleted()
    {
        var store = CreateStore();
        var me = NewUser();
        var stranger = NewUser();
        var foreign = await AddForeignSharedViewAsync(store, stranger, me, Name);

        var mine = await store.UpsertAsync(me, Name, Accounts());
        await store.UpsertAsync(me, Name, Products());
        await store.DeleteAsync(me, Name);

        Assert.NotEqual(foreign, mine);
        Assert.Equal(new[] { foreign }, (await ForeignIdsAsync(store, stranger, Name)).ToArray());
        Assert.Empty(await IdsOfAsync(store, me, Name));
        await store.DeleteAsync(stranger, Name);
    }

    /// <summary>
    /// Verifies that delete removes only the exact name.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesOnlyTheExactName()
    {
        var store = CreateStore();
        var me = NewUser();
        await store.UpsertAsync(me, Name, Accounts());
        var other = await store.UpsertAsync(me, "My own saved view", Accounts());

        await store.DeleteAsync(me, Name);

        Assert.Empty(await IdsOfAsync(store, me, Name));
        Assert.Equal(new[] { other }, (await IdsOfAsync(store, me, "My own saved view")).ToArray());
        await store.DeleteAsync(me, "My own saved view");
    }

    /// <summary>
    /// Verifies that deleting when there is nothing is silent.
    /// </summary>
    [Fact]
    public async Task DeletingWhenThereIsNothing_IsSilent()
    {
        await CreateStore().DeleteAsync(NewUser(), Name);
    }
}
