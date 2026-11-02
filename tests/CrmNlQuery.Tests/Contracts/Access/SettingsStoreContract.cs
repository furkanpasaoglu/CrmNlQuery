using CrmNlQuery.Application.Operations.Abstractions;

namespace CrmNlQuery.Tests.Contracts.Access;

/// <summary>
/// Shared tests that every settings store implementation (in-memory and PostgreSQL) must pass.
/// </summary>
public abstract class SettingsStoreContract : IAsyncLifetime
{
    private readonly List<string> _keys = new List<string>();

    protected abstract ISettingsStore CreateStore();

    /// <summary>
    /// Prepares the settings store for a test.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Cleans up the settings store after a test.
    /// </summary>
    public async Task DisposeAsync()
    {
        var store = CreateStore();
        foreach (var key in _keys)
        {
            await store.RemoveAsync(key);
        }
    }

    private string NewKey()
    {
        var key = "zz.test." + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        return key;
    }

    /// <summary>
    /// Verifies that a value round-trips through the store.
    /// </summary>
    [Fact]
    public async Task AValue_RoundTrips()
    {
        var store = CreateStore();
        var key = NewKey();

        await store.SetAsync(key, "42", @"contoso\admin", DateTimeOffset.UtcNow);

        var all = await store.GetAllAsync();
        Assert.Equal("42", all[key]);
    }

    /// <summary>
    /// Verifies that setting again replaces the value.
    /// </summary>
    [Fact]
    public async Task SettingAgain_ReplacesTheValue()
    {
        var store = CreateStore();
        var key = NewKey();
        await store.SetAsync(key, "1", "a", DateTimeOffset.UtcNow);

        await store.SetAsync(key, "2", "b", DateTimeOffset.UtcNow);

        Assert.Equal("2", (await store.GetAllAsync())[key]);
    }

    /// <summary>
    /// Verifies that removing a value forgets it and removing a missing one is harmless.
    /// </summary>
    [Fact]
    public async Task RemovingAValue_ForgetsIt_AndRemovingAMissingOneIsHarmless()
    {
        var store = CreateStore();
        var key = NewKey();
        await store.SetAsync(key, "1", "a", DateTimeOffset.UtcNow);

        await store.RemoveAsync(key);
        await store.RemoveAsync(key);

        Assert.False((await store.GetAllAsync()).ContainsKey(key));
    }

    /// <summary>
    /// Verifies that SQL looking values are stored as plain text.
    /// </summary>
    [Fact]
    public async Task SqlLookingValues_AreStoredAsPlainText()
    {
        var store = CreateStore();
        var key = NewKey();
        var evil = "x'); DROP TABLE runtime_settings; --";

        await store.SetAsync(key, evil, "a", DateTimeOffset.UtcNow);

        Assert.Equal(evil, (await store.GetAllAsync())[key]);
    }
}
