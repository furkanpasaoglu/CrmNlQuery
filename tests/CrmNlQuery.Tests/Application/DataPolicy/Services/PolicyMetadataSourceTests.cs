using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.DataPolicy.Services;

/// <summary>
/// Tests the filtering schema provider: closed tables and fields never reach the model, validator or explanation, and primary id and name are locked.
/// </summary>
public class PolicyMetadataSourceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AttributeInfo A(string name, string label, string type = "String") => new AttributeInfo(name, label, type, new List<OptionInfo>());

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Account Name"), A("address1_city", "City"), A("new_iban", "IBAN"), A("revenue", "Revenue", "Money") },
        new List<RelationshipInfo>
        {
            new RelationshipInfo("contact_account", "contact", "parentcustomerid", "account", "accountid"),
            new RelationshipInfo("patient_account", "new_patient", "new_accountid", "account", "accountid"),
        })
    {
        ManyToMany = new List<ManyToManyInfo>
        {
            new ManyToManyInfo("account_contact", "new_account_contact", "account", "accountid", "contact", "contactid"),
            new ManyToManyInfo("account_patient", "new_account_patient", "account", "accountid", "new_patient", "new_patientid"),
        },
    };

    private static readonly EntityInfo Contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
        new List<AttributeInfo> { A("contactid", "Contact", "Uniqueidentifier"), A("fullname", "Full Name"), A("parentcustomerid", "Company", "Customer"), A("new_tckimlikno", "TC Kimlik No") },
        new List<RelationshipInfo>());

    private static readonly EntityInfo Patient = new EntityInfo("new_patient", "new_patients", "Patient", "new_patientid", "new_name",
        new List<AttributeInfo> { A("new_patientid", "Patient", "Uniqueidentifier"), A("new_name", "Name"), A("new_diagnosis", "Diagnosis"), A("new_accountid", "Account", "Lookup") },
        new List<RelationshipInfo>());

    private static readonly EntityInfo Intersect = new EntityInfo("new_account_contact", "new_account_contacts", "Account-Contact", "new_account_contactid", "new_account_contactid",
        new List<AttributeInfo> { A("new_account_contactid", "Id", "Uniqueidentifier"), A("accountid", "Account", "Uniqueidentifier"), A("contactid", "Contact", "Uniqueidentifier") },
        new List<RelationshipInfo>());

    private static readonly EntityInfo PatientIntersect = new EntityInfo("new_account_patient", "new_account_patients", "Account-Patient", "new_account_patientid", "new_account_patientid",
        new List<AttributeInfo> { A("new_account_patientid", "Id", "Uniqueidentifier") }, new List<RelationshipInfo>());

    private sealed class CountingSource : IMetadataSource
    {
        private readonly FakeMetadataSource _inner = new FakeMetadataSource(Account, Contact, Patient, Intersect, PatientIntersect);
        public int Refreshes { get; private set; }
        public int Gets { get; private set; }
        /// <summary>
        /// Test fake that returns the list of entity summaries.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => _inner.ListEntitiesAsync(ct);
        /// <summary>
        /// Test fake that returns entity metadata by logical name.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) { Gets++; return _inner.GetEntityAsync(logicalName, ct); }
        /// <summary>
        /// Test fake metadata refresh that does nothing.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) { Refreshes++; return Task.CompletedTask; }
    }

    private sealed class FixedPolicy : IDataPolicy
    {
        public DataPolicySnapshot Snapshot { get; set; }
        public FixedPolicy(DataPolicySnapshot snapshot) { Snapshot = snapshot; }
        /// <summary>
        /// Test fake that returns a fixed data policy snapshot.
        /// </summary>
        public Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(Snapshot);
    }

    private static DataPolicyRule Rule(string kind, string entity, string? attribute, bool allowed) => new DataPolicyRule(kind, entity, attribute, allowed, "admin", Now);

    private static readonly string[] Defaults = { "account", "contact" };

    private static PolicyMetadataSource Make(out FixedPolicy policy, out CountingSource inner, params DataPolicyRule[] rules)
    {
        inner = new CountingSource();
        policy = new FixedPolicy(new DataPolicySnapshot(rules.ToList(), Defaults));
        return new PolicyMetadataSource(inner, policy);
    }

    /// <summary>
    /// Verifies that only open tables are listed and closed tables and intersect tables are not.
    /// </summary>
    [Fact]
    public async Task OnlyOpenTables_AreListed_ClosedOnesAndIntersectTablesAreNot()
    {
        var source = Make(out _, out _);

        var names = (await source.ListEntitiesAsync()).Select(e => e.LogicalName).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "account", "contact" }, names);
    }

    /// <summary>
    /// Verifies that a closed table is unknown to the model and the validator.
    /// </summary>
    [Fact]
    public async Task AClosedTable_IsUnknown_ToTheModelAndTheValidator()
    {
        var source = Make(out _, out _);

        Assert.Null(await source.GetEntityAsync("new_patient"));
        Assert.Null(await source.GetEntityAsync("does_not_exist"));
        Assert.NotNull(await source.GetEntityAsync("account"));
    }

    /// <summary>
    /// Verifies that a table opened by an admin becomes visible with its fields.
    /// </summary>
    [Fact]
    public async Task ATableTheAdminOpened_BecomesVisible_WithItsFields()
    {
        var source = Make(out _, out _, Rule("table", "new_patient", null, true));

        Assert.Contains("new_patient", (await source.ListEntitiesAsync()).Select(e => e.LogicalName));
        Assert.Contains(await source.GetEntityAsync("new_patient").ContinueWith(t => t.Result!.Attributes), a => a.LogicalName == "new_diagnosis");
    }

    /// <summary>
    /// Verifies that a sensitive field is hidden automatically and an admin can open it again.
    /// </summary>
    [Fact]
    public async Task ASensitiveFieldIsHiddenAutomatically_AndAnAdminCanOpenItAgain()
    {
        var hidden = await Make(out _, out _).GetEntityAsync("account");
        var opened = await Make(out _, out _, Rule("field", "account", "new_iban", true)).GetEntityAsync("account");

        Assert.DoesNotContain(hidden!.Attributes, a => a.LogicalName == "new_iban");
        Assert.Contains(opened!.Attributes, a => a.LogicalName == "new_iban");
        Assert.Contains(hidden.Attributes, a => a.LogicalName == "address1_city");
    }

    /// <summary>
    /// Verifies that an admin-closed field is removed but the primary id and name stay locked.
    /// </summary>
    [Fact]
    public async Task AnAdminClosedField_IsRemoved_ButThePrimaryIdAndNameAreLocked()
    {
        var entity = await Make(out _, out _, Rule("field", "account", "revenue", false), Rule("field", "account", "name", false), Rule("field", "account", "accountid", false))
            .GetEntityAsync("account");

        Assert.DoesNotContain(entity!.Attributes, a => a.LogicalName == "revenue");
        Assert.Contains(entity.Attributes, a => a.LogicalName == "name");
        Assert.Contains(entity.Attributes, a => a.LogicalName == "accountid");
    }

    /// <summary>
    /// Verifies that relationships to closed tables or through closed fields are removed.
    /// </summary>
    [Fact]
    public async Task RelationshipsToClosedTables_OrThroughClosedFields_AreRemoved()
    {
        var plain = await Make(out _, out _).GetEntityAsync("account");
        var withClosedLookup = await Make(out _, out _, Rule("field", "contact", "parentcustomerid", false)).GetEntityAsync("account");

        Assert.Contains(plain!.Relationships, r => r.ReferencingEntity == "contact");
        Assert.DoesNotContain(plain.Relationships, r => r.ReferencingEntity == "new_patient");
        Assert.DoesNotContain(withClosedLookup!.Relationships, r => r.ReferencingEntity == "contact");
    }

    /// <summary>
    /// Verifies that many to many between open tables stays and its intersect table is implicitly open.
    /// </summary>
    [Fact]
    public async Task ManyToMany_BetweenOpenTablesStays_AndItsIntersectTableIsImplicitlyOpen()
    {
        var source = Make(out _, out _);

        var account = await source.GetEntityAsync("account");
        var intersect = await source.GetEntityAsync("new_account_contact");
        var closedIntersect = await source.GetEntityAsync("new_account_patient");

        Assert.Contains(account!.ManyToMany, m => m.IntersectEntity == "new_account_contact");
        Assert.DoesNotContain(account.ManyToMany, m => m.IntersectEntity == "new_account_patient");
        Assert.NotNull(intersect);
        Assert.Null(closedIntersect);
    }

    /// <summary>
    /// Verifies that when the switch is off nothing is filtered.
    /// </summary>
    [Fact]
    public async Task WhenTheSwitchIsOff_NothingIsFiltered()
    {
        var source = Make(out var policy, out _);
        policy.Snapshot = policy.Snapshot.WithEnabled(false);

        Assert.NotNull(await source.GetEntityAsync("new_patient"));
        Assert.Contains(await source.GetEntityAsync("account").ContinueWith(t => t.Result!.Attributes), a => a.LogicalName == "new_iban");
        Assert.Equal(5, (await source.ListEntitiesAsync()).Count);
    }

    /// <summary>
    /// Verifies that a rule change takes effect on the next call.
    /// </summary>
    [Fact]
    public async Task ARuleChange_TakesEffectOnTheNextCall()
    {
        var source = Make(out var policy, out _);
        Assert.Null(await source.GetEntityAsync("new_patient"));

        policy.Snapshot = new DataPolicySnapshot(new List<DataPolicyRule> { Rule("table", "new_patient", null, true) }, Defaults);

        Assert.NotNull(await source.GetEntityAsync("new_patient"));
    }

    /// <summary>
    /// Verifies that refresh is forwarded to the underlying source.
    /// </summary>
    [Fact]
    public async Task Refresh_IsForwarded()
    {
        var source = Make(out _, out var inner);

        await source.RefreshAsync();

        Assert.Equal(1, inner.Refreshes);
    }
}
