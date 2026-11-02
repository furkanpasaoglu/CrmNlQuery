using CrmNlQuery.Application.Preferences.Abstractions;

namespace CrmNlQuery.Tests.Contracts.Access;

/// <summary>
/// Shared contract tests every preferences store must pass: preferences belong only to their owner.
/// </summary>
public abstract class PreferencesStoreContract
{
    protected abstract IPreferencesStore CreateStore();

    /// <summary>
    /// Verifies that a new user has no name.
    /// </summary>
    [Fact]
    public async Task ANewUser_HasNoName()
    {
        Assert.Null(await CreateStore().GetDisplayNameAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Verifies that the name is stored exactly and can be changed.
    /// </summary>
    [Fact]
    public async Task TheName_IsStoredExactly_AndCanBeChanged()
    {
        var store = CreateStore();
        var me = Guid.NewGuid();

        await store.SetDisplayNameAsync(me, "Deniz");
        Assert.Equal("Deniz", await store.GetDisplayNameAsync(me));

        await store.SetDisplayNameAsync(me, "O'Brien \"Şırnak\" <b>x</b>; DROP TABLE x; --");
        Assert.Equal("O'Brien \"Şırnak\" <b>x</b>; DROP TABLE x; --", await store.GetDisplayNameAsync(me));
    }

    /// <summary>
    /// Verifies that a null name clears it.
    /// </summary>
    [Fact]
    public async Task ANullName_ClearsIt()
    {
        var store = CreateStore();
        var me = Guid.NewGuid();
        await store.SetDisplayNameAsync(me, "Deniz");

        await store.SetDisplayNameAsync(me, null);

        Assert.Null(await store.GetDisplayNameAsync(me));
    }

    /// <summary>
    /// Verifies that every user has their own name nobody reads or changes anothers.
    /// </summary>
    [Fact]
    public async Task EveryUserHasTheirOwnName_NobodyReadsOrChangesAnothers()
    {
        var store = CreateStore();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await store.SetDisplayNameAsync(a, "Ayşe");
        await store.SetDisplayNameAsync(b, "Burak");
        await store.SetDisplayNameAsync(a, null);

        Assert.Null(await store.GetDisplayNameAsync(a));
        Assert.Equal("Burak", await store.GetDisplayNameAsync(b));
    }

    /// <summary>
    /// Verifies that a new account has no language and the language can be set changed and cleared.
    /// </summary>
    [Fact]
    public async Task ANewAccount_HasNoLanguage_AndTheLanguageCanBeSetChangedAndCleared()
    {
        var store = CreateStore();
        var account = "contoso\\" + Guid.NewGuid().ToString("N");
        Assert.Null(await store.GetLanguageAsync(account));

        await store.SetLanguageAsync(account, "tr");
        Assert.Equal("tr", await store.GetLanguageAsync(account));
        await store.SetLanguageAsync(account, "en");
        Assert.Equal("en", await store.GetLanguageAsync(account));
        await store.SetLanguageAsync(account, null);
        Assert.Null(await store.GetLanguageAsync(account));
    }

    /// <summary>
    /// Verifies that every account has its own language and the language is independent of the display name.
    /// </summary>
    [Fact]
    public async Task EveryAccountHasItsOwnLanguage_AndTheLanguageIsIndependentOfTheDisplayName()
    {
        var store = CreateStore();
        var user = Guid.NewGuid();
        var a = "contoso\\" + Guid.NewGuid().ToString("N");
        var b = "contoso\\" + Guid.NewGuid().ToString("N");

        await store.SetLanguageAsync(a, "tr");
        await store.SetLanguageAsync(b, "en");
        await store.SetDisplayNameAsync(user, "Ayşe");
        await store.SetDisplayNameAsync(user, null);

        Assert.Equal("tr", await store.GetLanguageAsync(a));
        Assert.Equal("en", await store.GetLanguageAsync(b));
    }
}
