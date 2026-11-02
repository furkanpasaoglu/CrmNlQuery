using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Application.Operations.Settings;

namespace CrmNlQuery.Tests.Application.DataPolicy;

/// <summary>
/// Tests data policy hardening findings: lookup columns, valueof, order alias, caching, setting key permission, Turkish labels, races and first-load failure.
/// </summary>
public class DataPolicyHardeningTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AttributeInfo A(string name, string label, string type = "String") => new AttributeInfo(name, label, type, new List<OptionInfo>());

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Name"), A("address1_city", "City"), A("new_iban", "IBAN") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Patient = new EntityInfo("new_patient", "new_patients", "Patient", "new_patientid", "new_name",
        new List<AttributeInfo> { A("new_patientid", "Patient", "Uniqueidentifier"), A("new_name", "Name"), A("new_diagnosis", "Diagnosis") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
        new List<AttributeInfo>
        {
            A("incidentid", "Case", "Uniqueidentifier"), A("title", "Title"),
            A("new_patientid", "Patient", "Lookup"), A("new_patientidname", "Patient name"), A("new_patientidyominame", "Patient yomi"),
            A("ownerid", "Owner", "Owner"), A("owneridname", "Owner name"),
            A("customerid", "Customer", "Customer"), A("customeridname", "Customer name"), A("customeridtype", "Customer type", "EntityName"),
            A("new_accountid", "Account", "Lookup"), A("new_accountidname", "Account name"),
        },
        new List<RelationshipInfo>
        {
            new RelationshipInfo("incident_patient", "incident", "new_patientid", "new_patient", "new_patientid"),
            new RelationshipInfo("incident_owner", "incident", "ownerid", "systemuser", "systemuserid"),
            new RelationshipInfo("incident_customer_patient", "incident", "customerid", "new_patient", "new_patientid"),
            new RelationshipInfo("incident_customer_account", "incident", "customerid", "account", "accountid"),
            new RelationshipInfo("incident_account", "incident", "new_accountid", "account", "accountid"),
        });

    private sealed class FixedPolicy : IDataPolicy
    {
        public DataPolicySnapshot Snapshot { get; set; }
        public FixedPolicy(DataPolicySnapshot snapshot) { Snapshot = snapshot; }
        /// <summary>
        /// Returns the data policy snapshot used by the test.
        /// </summary>
        public Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(Snapshot);
    }

    private static IMetadataSource Filtered(params DataPolicyRule[] rules)
        => new PolicyMetadataSource(new FakeMetadataSource(Account, Patient, Incident), new FixedPolicy(new DataPolicySnapshot(rules.ToList(), new[] { "account", "incident" })));

    private static DataPolicyRule Rule(string kind, string entity, string? attribute, bool allowed) => new DataPolicyRule(kind, entity, attribute, allowed, "admin", Now);

    /// <summary>
    /// Verifies that a lookup to a closed table is hidden along with its name attributes while other lookups stay.
    /// </summary>
    [Fact]
    public async Task ALookupToAClosedTable_IsHidden_WithItsNameAttributes_ButOtherLookupsStay()
    {
        var incident = (await Filtered().GetEntityAsync("incident"))!;
        var names = incident.Attributes.Select(a => a.LogicalName).ToList();

        Assert.DoesNotContain("new_patientid", names);
        Assert.DoesNotContain("new_patientidname", names);
        Assert.DoesNotContain("new_patientidyominame", names);
        Assert.Contains("new_accountid", names);
        Assert.Contains("new_accountidname", names);
        Assert.Contains("customerid", names);
        Assert.Contains("incidentid", names);
    }

    /// <summary>
    /// Verifies that owner columns pointing at the closed system user table stay as name only lookups.
    /// </summary>
    [Fact]
    public async Task OwnerColumns_PointingAtTheClosedSystemUserTable_StayAsNameOnlyLookups()
    {
        var names = (await Filtered().GetEntityAsync("incident"))!.Attributes.Select(a => a.LogicalName).ToList();

        Assert.Contains("ownerid", names);
        Assert.Contains("owneridname", names);
    }

    /// <summary>
    /// Verifies that the real ownerid relationship targeting the abstract owner entity keeps the owner column.
    /// </summary>
    [Fact]
    public async Task TheRealOwnerRelationship_TargetingTheAbstractOwnerEntity_KeepsTheOwnerColumn()
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Name"), A("ownerid", "Owner", "Owner"), A("owneridname", "Owner name") },
            new List<RelationshipInfo> { new RelationshipInfo("owner_accounts", "account", "ownerid", "owner", "ownerid") });
        var source = new PolicyMetadataSource(new FakeMetadataSource(account), new FixedPolicy(new DataPolicySnapshot(new List<DataPolicyRule>(), new[] { "account" })));

        var names = (await source.GetEntityAsync("account"))!.Attributes.Select(a => a.LogicalName).ToList();

        Assert.Contains("ownerid", names);
        Assert.Contains("owneridname", names);
    }

    /// <summary>
    /// Verifies that a query that selects or filters the hidden lookup is rejected.
    /// </summary>
    [Fact]
    public async Task AQueryThatSelectsOrFiltersTheHiddenLookup_IsRejected()
    {
        var validator = new PlanValidator(Filtered());

        var column = await validator.ValidateFetchXmlAsync("<fetch><entity name=\"incident\"><attribute name=\"title\" /><attribute name=\"new_patientid\" /></entity></fetch>");
        var filter = await validator.ValidateFetchXmlAsync("<fetch><entity name=\"incident\"><attribute name=\"title\" /><filter><condition attribute=\"new_patientid\" operator=\"not-null\" /></filter></entity></fetch>");
        var open = await validator.ValidateFetchXmlAsync("<fetch><entity name=\"incident\"><attribute name=\"title\" /><attribute name=\"new_accountid\" /></entity></fetch>");

        Assert.False(column.IsValid);
        Assert.False(filter.IsValid);
        Assert.True(open.IsValid, string.Join("; ", open.Errors));
    }

    /// <summary>
    /// Verifies that when an admin opens the patient table the lookup comes back.
    /// </summary>
    [Fact]
    public async Task WhenAnAdminOpensThePatientTable_TheLookupComesBack()
    {
        var names = (await Filtered(Rule("table", "new_patient", null, true)).GetEntityAsync("incident"))!.Attributes.Select(a => a.LogicalName).ToList();

        Assert.Contains("new_patientid", names);
        Assert.Contains("new_patientidname", names);
    }

    /// <summary>
    /// Verifies that valueof is rejected because it compares two columns.
    /// </summary>
    [Fact]
    public async Task ValueOf_IsRejected_BecauseItComparesTwoColumns()
    {
        var validator = new PlanValidator(Filtered());

        var r = await validator.ValidateFetchXmlAsync("<fetch><entity name=\"account\"><attribute name=\"name\" /><filter><condition attribute=\"name\" operator=\"eq\" value=\"x\" valueof=\"new_iban\" /></filter></entity></fetch>");

        Assert.False(r.IsValid);
    }

    /// <summary>
    /// Verifies that an order with an alias and a closed attribute is rejected because the attribute is always checked.
    /// </summary>
    [Fact]
    public async Task AnOrderWithAnAliasAndAClosedAttribute_IsRejected_TheAttributeIsAlwaysChecked()
    {
        var validator = new PlanValidator(Filtered());

        var closed = await validator.ValidateFetchXmlAsync("<fetch><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"new_iban\" alias=\"a\" /></entity></fetch>");
        var unknown = await validator.ValidateFetchXmlAsync("<fetch><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"nope\" alias=\"a\" /></entity></fetch>");

        Assert.False(closed.IsValid);
        Assert.False(unknown.IsValid);
    }

    /// <summary>
    /// Verifies that the stored query check also refuses valueof.
    /// </summary>
    [Fact]
    public async Task TheStoredQueryCheck_AlsoRefusesValueOf()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><filter><condition attribute=\"name\" operator=\"eq\" value=\"x\" valueof=\"address1_city\" /></filter></entity></fetch>";

        Assert.False(await StoredQueryCheck.IsUsableAsync(xml, Filtered()));
    }

    private static readonly EntityInfo LinkedA = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Name") }, new List<RelationshipInfo>())
    {
        ManyToMany = new List<ManyToManyInfo> { new ManyToManyInfo("account_contact", "new_account_contact", "account", "accountid", "contact", "contactid") },
    };

    private static readonly EntityInfo LinkedB = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
        new List<AttributeInfo> { A("contactid", "Contact", "Uniqueidentifier"), A("fullname", "Name") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Intersect = new EntityInfo("new_account_contact", "new_account_contacts", "A-C", "new_account_contactid", "new_account_contactid",
        new List<AttributeInfo> { A("new_account_contactid", "Id", "Uniqueidentifier") }, new List<RelationshipInfo>());

    /// <summary>
    /// Verifies that an intersect table the admin explicitly closed stays closed even between two open tables.
    /// </summary>
    [Fact]
    public async Task AnIntersectTableTheAdminClosedExplicitly_StaysClosed_EvenBetweenTwoOpenTables()
    {
        var inner = new FakeMetadataSource(LinkedA, LinkedB, Intersect);
        IMetadataSource Make(params DataPolicyRule[] rules) => new PolicyMetadataSource(inner, new FixedPolicy(new DataPolicySnapshot(rules.ToList(), new[] { "account", "contact" })));

        Assert.NotNull(await Make().GetEntityAsync("new_account_contact"));
        Assert.Null(await Make(Rule("table", "new_account_contact", null, false)).GetEntityAsync("new_account_contact"));
    }

    /// <summary>
    /// Verifies that a previous query that now touches closed data is dropped so the model never sees the closed name.
    /// </summary>
    [Fact]
    public async Task APreviousQueryThatNowTouchesClosedData_IsDropped_SoTheModelNeverSeesTheClosedName()
    {
        var closedNow = new PreviousQuery("cities", "account", "<fetch><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"new_iban\" /></entity></fetch>");
        var fine = new PreviousQuery("names", "account", "<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>");

        Assert.Null(await PreviousQueryGuard.FilterAsync(closedNow, Filtered()));
        Assert.Same(fine, await PreviousQueryGuard.FilterAsync(fine, Filtered()));
        Assert.Null(await PreviousQueryGuard.FilterAsync(null, Filtered()));
    }

    private sealed class CountingPlanner : IPlanGenerator
    {
        public int Calls { get; private set; }
        /// <summary>
        /// Generates a plan using the stub generator in the test.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PlanOutcome(true, "<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", new List<string>()));
        }
    }

    /// <summary>
    /// Verifies that a cached plan is not reused when the policy version changes.
    /// </summary>
    [Fact]
    public async Task ACachedPlan_IsNotReused_WhenThePolicyVersionChanges()
    {
        var version = "v1";
        var inner = new CountingPlanner();
        var cache = new CachingPlanGenerator(inner, TimeSpan.FromMinutes(5), policyVersion: ct => Task.FromResult(version));
        var request = new PlanRequest("accounts", new List<EntityInfo> { Account }, new List<ResolvedRecord>(), new List<string> { "name" }, false, null, null, null);

        await cache.GenerateAsync(request);
        await cache.GenerateAsync(request);
        Assert.Equal(1, inner.Calls);

        version = "v2";
        await cache.GenerateAsync(request);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that the data policy switch needs the data policy permission while other settings do not.
    /// </summary>
    [Fact]
    public void TheDataPolicySwitch_NeedsTheDataPolicyPermission_OtherSettingsDoNot()
    {
        Assert.Equal(Permissions.DataPolicyManage, SettingsCatalog.RequiredPermission(SettingsCatalog.DataPolicy));
        Assert.Null(SettingsCatalog.RequiredPermission(SettingsCatalog.AskEnabled));
        Assert.Null(SettingsCatalog.RequiredPermission("Limits:QuestionsPerDay"));
    }

    /// <summary>
    /// Verifies that Turkish labels and standard identifier fields are recognized as sensitive or not as expected.
    /// </summary>
    [Theory]
    [InlineData("ownerid", "Sahip Kimliği")]
    [InlineData("campaignid", "Kampanya Kimliği")]
    [InlineData("new_islem", "İşlem Kimliği")]
    [InlineData("new_x", "Müşteri Kimliği")]
    public void OrdinaryTurkishIdLabels_AreNotFlagged(string logicalName, string label)
    {
        Assert.False(SensitiveNames.IsSensitive(logicalName, label));
    }

    /// <summary>
    /// Verifies that sensitive-name detection works for Turkish and English identity-like field labels.
    /// </summary>
    [Theory]
    [InlineData("new_k", "Kimlik Numarası")]
    [InlineData("new_k2", "T.C. Kimlik No")]
    [InlineData("governmentid", "Government ID")]
    [InlineData("new_vergi", "Tax ID")]
    [InlineData("new_n", "National ID")]
    public void NationalAndTaxIdFields_AreFlagged(string logicalName, string label)
    {
        Assert.True(SensitiveNames.IsSensitive(logicalName, label));
    }

    private sealed class GateStore : IDataPolicyStore
    {
        public int Lists { get; private set; }
        public bool Broken { get; set; }
        public TaskCompletionSource<bool>? Hold { get; set; }
        public List<DataPolicyRule> Rules { get; } = new List<DataPolicyRule>();
        public bool Persistent => false;

        /// <summary>
        /// Lists the rules held by the in-memory data policy store.
        /// </summary>
        public async Task<IReadOnlyList<DataPolicyRule>> ListAsync(CancellationToken ct = default)
        {
            Lists++;
            if (Broken) throw new InvalidOperationException("db down");
            var snapshot = Rules.ToList();
            if (Hold is not null) await Hold.Task;
            return snapshot;
        }

        /// <summary>
        /// Stores a data policy rule in the in-memory store.
        /// </summary>
        public Task SetAsync(DataPolicyRule rule, CancellationToken ct = default) { Rules.Add(rule); return Task.CompletedTask; }
        /// <summary>
        /// Removes a data policy rule from the in-memory store.
        /// </summary>
        public Task<bool> RemoveAsync(string kind, string entity, string? attribute, CancellationToken ct = default) => Task.FromResult(false);
    }

    /// <summary>
    /// Verifies that if the very first load fails the service fails closed instead of falling back to defaults.
    /// </summary>
    [Fact]
    public async Task WhenTheVeryFirstLoadFails_TheServiceFailsClosed_InsteadOfFallingBackToDefaults()
    {
        var store = new GateStore { Broken = true };
        var service = new DataPolicyService(store, () => new[] { "account" }, () => true, () => Now, TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync());
    }

    /// <summary>
    /// Verifies that a reload started before a change never keeps the old rules after invalidation.
    /// </summary>
    [Fact]
    public async Task AReloadThatStartedBeforeAChange_NeverKeepsTheOldRulesAfterInvalidate()
    {
        var store = new GateStore { Hold = new TaskCompletionSource<bool>() };
        var service = new DataPolicyService(store, () => new[] { "account" }, () => true, () => Now, TimeSpan.FromSeconds(30));

        var loading = service.GetAsync();
        await store.SetAsync(new DataPolicyRule("table", "new_patient", null, true, "admin", Now));
        service.Invalidate();
        store.Hold.SetResult(true);
        await loading;

        store.Hold = null;
        var after = await service.GetAsync();

        Assert.True(after.IsTableOpen("new_patient"));
        Assert.Equal(2, store.Lists);
    }

    /// <summary>
    /// Verifies that the memory store reports that it is not persistent.
    /// </summary>
    [Fact]
    public void TheMemoryStore_SaysItIsNotPersistent()
    {
        Assert.False(new MemoryDataPolicyStore().Persistent);
    }
}
