using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Tests.Contracts.Access;

/// <summary>
/// Defines the shared behavior every data policy store implementation must satisfy.
/// </summary>
public abstract class DataPolicyStoreContract
{
    protected abstract IDataPolicyStore CreateStore();

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 10);

    /// <summary>
    /// Verifies that a rule is stored and listed with its fields.
    /// </summary>
    [Fact]
    public async Task ARuleIsStoredAndListed_WithItsFields()
    {
        var store = CreateStore();
        var table = Unique("new_t");
        await store.SetAsync(new DataPolicyRule("table", table, null, true, "CONTOSO\\admin", Now));
        await store.SetAsync(new DataPolicyRule("field", table, "new_x", false, "CONTOSO\\admin", Now));

        var rules = (await store.ListAsync()).Where(r => r.Entity == table).ToList();

        Assert.Equal(2, rules.Count);
        var t = rules.Single(r => r.Kind == "table");
        Assert.True(t.Allowed);
        Assert.Null(t.Attribute);
        Assert.Equal("CONTOSO\\admin", t.SetBy);
        var f = rules.Single(r => r.Kind == "field");
        Assert.Equal("new_x", f.Attribute);
        Assert.False(f.Allowed);
    }

    /// <summary>
    /// Verifies that setting the same key again replaces case insensitively.
    /// </summary>
    [Fact]
    public async Task SettingTheSameKeyAgain_Replaces_CaseInsensitively()
    {
        var store = CreateStore();
        var table = Unique("new_t");
        await store.SetAsync(new DataPolicyRule("field", table, "new_x", false, "a", Now));
        await store.SetAsync(new DataPolicyRule("field", table.ToUpperInvariant(), "NEW_X", true, "b", Now.AddMinutes(1)));

        var rules = (await store.ListAsync()).Where(r => string.Equals(r.Entity, table, StringComparison.OrdinalIgnoreCase)).ToList();

        var rule = Assert.Single(rules);
        Assert.True(rule.Allowed);
        Assert.Equal("b", rule.SetBy);
    }

    /// <summary>
    /// Verifies that removing a rule reports whether it existed.
    /// </summary>
    [Fact]
    public async Task RemovingARule_ReportsWhetherItExisted()
    {
        var store = CreateStore();
        var table = Unique("new_t");
        await store.SetAsync(new DataPolicyRule("table", table, null, false, "a", Now));

        Assert.True(await store.RemoveAsync("table", table, null));
        Assert.False(await store.RemoveAsync("table", table, null));
        Assert.DoesNotContain((await store.ListAsync()), r => r.Entity == table);
    }

    /// <summary>
    /// Verifies that invalid rules are rejected.
    /// </summary>
    [Theory]
    [InlineData("view", "account", null)]
    [InlineData("field", "account", null)]
    [InlineData("table", "account", "name")]
    [InlineData("table", "", null)]
    public async Task InvalidRules_AreRejected(string kind, string entity, string? attribute)
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(new DataPolicyRule(kind, entity, attribute, true, "a", Now)));
    }
}
