using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Models;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.DataPolicy;

/// <summary>
/// Tests the sensitive name rule, the table and field decisions where admin choices win, and the cached policy snapshot.
/// </summary>
public class DataPolicyCoreTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static DataPolicyRule Rule(string kind, string entity, string? attribute, bool allowed)
        => new DataPolicyRule(kind, entity, attribute, allowed, "admin", Now);

    private static readonly string[] Defaults = { "account", "contact" };

    /// <summary>
    /// Verifies that sensitive names are recognised by logical name or label.
    /// </summary>
    [Theory]
    [InlineData("new_tckimlikno", null)]
    [InlineData("new_tc", null)]
    [InlineData("tckn", null)]
    [InlineData("new_tcno", "Kimlik")]
    [InlineData("new_iban", "IBAN")]
    [InlineData("new_x1", "Maaş")]
    [InlineData("new_maas", null)]
    [InlineData("new_dt", "Doğum Tarihi")]
    [InlineData("new_kredikartino", null)]
    [InlineData("ssn", null)]
    [InlineData("salary", null)]
    [InlineData("passportnumber", null)]
    [InlineData("birthdate", "Birthday")]
    [InlineData("new_a", "T.C. Kimlik No")]
    [InlineData("new_b", "TC No")]
    public void SensitiveNames_AreRecognised_ByLogicalNameOrLabel(string logicalName, string? label)
    {
        Assert.True(SensitiveNames.IsSensitive(logicalName, label));
    }

    /// <summary>
    /// Verifies that ordinary names are not flagged even when they contain short sensitive letters.
    /// </summary>
    [Theory]
    [InlineData("etc", "Etc")]
    [InlineData("practice", "Practice")]
    [InlineData("new_ostc", null)]
    [InlineData("new_tcmb_rate", "Central bank rate")]
    [InlineData("classname", "Class name")]
    [InlineData("name", "Name")]
    [InlineData("address1_city", "Address 1: City")]
    [InlineData("creditlimit", "Credit Limit")]
    [InlineData("telephone1", "Business Phone")]
    public void OrdinaryNames_AreNotFlagged_EvenWhenTheyContainShortSensitiveLetters(string logicalName, string? label)
    {
        Assert.False(SensitiveNames.IsSensitive(logicalName, label));
    }

    /// <summary>
    /// Verifies that a table is open only when it is in the starting list or opened by an admin.
    /// </summary>
    [Fact]
    public void ATable_IsOpenOnlyWhenItIsInTheStartingList_OrOpenedByAnAdmin()
    {
        var snapshot = new DataPolicySnapshot(new List<DataPolicyRule> { Rule("table", "new_patient", null, true), Rule("table", "contact", null, false) }, Defaults);

        Assert.Equal(TableState.Open, snapshot.StateOfTable("account"));
        Assert.Equal(TableState.ClosedByDefault, snapshot.StateOfTable("prescription"));
        Assert.Equal(TableState.OpenByAdmin, snapshot.StateOfTable("new_patient"));
        Assert.Equal(TableState.ClosedByAdmin, snapshot.StateOfTable("contact"));
        Assert.True(snapshot.IsTableOpen("account"));
        Assert.True(snapshot.IsTableOpen("new_patient"));
        Assert.False(snapshot.IsTableOpen("prescription"));
        Assert.False(snapshot.IsTableOpen("contact"));
    }

    /// <summary>
    /// Verifies that names are case insensitive.
    /// </summary>
    [Fact]
    public void Names_AreCaseInsensitive()
    {
        var snapshot = new DataPolicySnapshot(new List<DataPolicyRule> { Rule("table", "NEW_Patient", null, true), Rule("field", "Account", "Name2", false) }, Defaults);

        Assert.True(snapshot.IsTableOpen("new_patient"));
        Assert.True(snapshot.IsTableOpen("ACCOUNT"));
        Assert.False(snapshot.IsFieldOpen("account", "NAME2", "Name 2"));
    }

    /// <summary>
    /// Verifies that a field is open by default closed automatically when sensitive and admin decisions win.
    /// </summary>
    [Fact]
    public void AField_IsOpenByDefault_ClosedAutomaticallyWhenSensitive_AndAdminDecisionsWin()
    {
        var snapshot = new DataPolicySnapshot(new List<DataPolicyRule>
        {
            Rule("field", "contact", "new_tckimlikno", true),
            Rule("field", "contact", "jobtitle", false),
        }, Defaults);

        Assert.Equal(FieldState.Open, snapshot.StateOfField("contact", "lastname", "Last Name"));
        Assert.Equal(FieldState.ClosedAutomatically, snapshot.StateOfField("contact", "new_iban", "IBAN"));
        Assert.Equal(FieldState.OpenByAdmin, snapshot.StateOfField("contact", "new_tckimlikno", "TC Kimlik"));
        Assert.Equal(FieldState.ClosedByAdmin, snapshot.StateOfField("contact", "jobtitle", "Job Title"));
        Assert.False(snapshot.IsFieldOpen("contact", "new_iban", "IBAN"));
        Assert.True(snapshot.IsFieldOpen("contact", "new_tckimlikno", "TC Kimlik"));
    }

    /// <summary>
    /// Verifies that the version changes with the rules and stays the same for the same input.
    /// </summary>
    [Fact]
    public void TheVersion_ChangesWithTheRules_AndStaysTheSameForTheSameInput()
    {
        var a = new DataPolicySnapshot(new List<DataPolicyRule> { Rule("field", "contact", "jobtitle", false) }, Defaults);
        var same = new DataPolicySnapshot(new List<DataPolicyRule> { Rule("field", "contact", "jobtitle", false) }, Defaults);
        var other = new DataPolicySnapshot(new List<DataPolicyRule>(), Defaults);
        var otherDefaults = new DataPolicySnapshot(new List<DataPolicyRule> { Rule("field", "contact", "jobtitle", false) }, new[] { "account" });

        Assert.Equal(a.Version, same.Version);
        Assert.NotEqual(a.Version, other.Version);
        Assert.NotEqual(a.Version, otherDefaults.Version);
        Assert.NotEqual(a.Version, a.WithEnabled(false).Version);
    }

    private sealed class CountingStore : IDataPolicyStore
    {
        private readonly MemoryDataPolicyStore _inner = new MemoryDataPolicyStore();
        public int Lists { get; private set; }
        public bool Broken { get; set; }
        public bool Persistent => false;
        /// <summary>
        /// Fake policy store method that counts list calls.
        /// </summary>
        public Task<IReadOnlyList<DataPolicyRule>> ListAsync(CancellationToken ct = default)
        {
            Lists++;
            if (Broken) throw new InvalidOperationException("db down");
            return _inner.ListAsync(ct);
        }
        /// <summary>
        /// Fake policy store method that records a saved rule.
        /// </summary>
        public Task SetAsync(DataPolicyRule rule, CancellationToken ct = default) => _inner.SetAsync(rule, ct);
        /// <summary>
        /// Fake policy store method that records a removed rule.
        /// </summary>
        public Task<bool> RemoveAsync(string kind, string entity, string? attribute, CancellationToken ct = default) => _inner.RemoveAsync(kind, entity, attribute, ct);
    }

    /// <summary>
    /// Verifies that the snapshot is cached within the ttl reloaded after it and on invalidate.
    /// </summary>
    [Fact]
    public async Task TheSnapshot_IsCachedWithinTheTtl_ReloadedAfterIt_AndOnInvalidate()
    {
        var store = new CountingStore();
        var clock = Now;
        var service = new DataPolicyService(store, () => Defaults, () => true, () => clock, TimeSpan.FromSeconds(30));

        await service.GetAsync();
        await service.GetAsync();
        Assert.Equal(1, store.Lists);

        clock = clock.AddSeconds(31);
        await service.GetAsync();
        Assert.Equal(2, store.Lists);

        service.Invalidate();
        await service.GetAsync();
        Assert.Equal(3, store.Lists);
    }

    /// <summary>
    /// Verifies that the on off switch is read on every call not cached.
    /// </summary>
    [Fact]
    public async Task TheOnOffSwitch_IsReadOnEveryCall_NotCached()
    {
        var enabled = true;
        var service = new DataPolicyService(new CountingStore(), () => Defaults, () => enabled, () => Now, TimeSpan.FromMinutes(5));

        Assert.True((await service.GetAsync()).Enabled);
        enabled = false;
        Assert.False((await service.GetAsync()).Enabled);
    }

    /// <summary>
    /// Verifies that when the store fails the previous snapshot is kept and without one the service fails closed.
    /// </summary>
    [Fact]
    public async Task WhenTheStoreFails_ThePreviousSnapshotIsKept_AndWithoutOneTheServiceFailsClosed()
    {
        var store = new CountingStore();
        await store.SetAsync(Rule("table", "new_patient", null, true));
        var clock = Now;
        var service = new DataPolicyService(store, () => Defaults, () => true, () => clock, TimeSpan.FromSeconds(30));
        Assert.True((await service.GetAsync()).IsTableOpen("new_patient"));

        store.Broken = true;
        clock = clock.AddSeconds(31);
        Assert.True((await service.GetAsync()).IsTableOpen("new_patient"));

        var fresh = new DataPolicyService(store, () => Defaults, () => true, () => clock, TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.GetAsync());
    }
}
