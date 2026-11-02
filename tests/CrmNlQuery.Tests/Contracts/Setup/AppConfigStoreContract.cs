using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Tests.Contracts.Setup;

/// <summary>
/// Behaviors every IAppConfigStore (in memory, PostgreSQL) must have.
/// </summary>
public abstract class AppConfigStoreContract
{
    protected abstract IAppConfigStore CreateStore();

    [Fact]
    public async Task SettingsAreStoredAndReadBackWithTheirSecretFlag()
    {
        var store = CreateStore();
        await store.DeleteAllAsync();

        await store.SetManyAsync(new[] { new AppConfigEntry("Crm:BaseUrl", "https://crm.example", false), new AppConfigEntry("Crm:Password", "v1:cipher", true) });
        var list = await store.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.Key == "Crm:BaseUrl" && e.Value == "https://crm.example" && !e.Secret);
        Assert.Contains(list, e => e.Key == "Crm:Password" && e.Value == "v1:cipher" && e.Secret);
    }

    [Fact]
    public async Task SettingTheSameKeyAgainReplacesIt()
    {
        var store = CreateStore();
        await store.DeleteAllAsync();
        await store.SetManyAsync(new[] { new AppConfigEntry("A", "1", false) });

        await store.SetManyAsync(new[] { new AppConfigEntry("A", "2", true) });

        var only = Assert.Single(await store.ListAsync());
        Assert.Equal("2", only.Value);
        Assert.True(only.Secret);
    }

    [Fact]
    public async Task DeleteAllEmptiesTheStore()
    {
        var store = CreateStore();
        await store.SetManyAsync(new[] { new AppConfigEntry("A", "1", false) });

        await store.DeleteAllAsync();

        Assert.Empty(await store.ListAsync());
    }
}
