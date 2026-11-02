using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.DataPolicy.Services;

/// <summary>
/// Tests the data policy admin logic: table and field lists, open and close with audit and cache refresh, and access checks.
/// </summary>
public class DataPolicyAdminServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AttributeInfo A(string name, string label, string type = "String") => new AttributeInfo(name, label, type, new List<OptionInfo>());

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Name"), A("address1_city", "City"), A("new_iban", "IBAN"), A("new_tckimlikno", "TC Kimlik No") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Patient = new EntityInfo("new_patient", "new_patients", "Patient", "new_patientid", "new_name",
        new List<AttributeInfo> { A("new_patientid", "Patient", "Uniqueidentifier"), A("new_name", "Name"), A("new_diagnosis", "Diagnosis") }, new List<RelationshipInfo>());

    private sealed class ProbeExecutor : IQueryExecutor
    {
        public List<string> Executed { get; } = new List<string>();
        public HashSet<string> Denied { get; } = new HashSet<string>();
        public bool Broken { get; set; }

        /// <summary>
        /// Executor that answers the access probe query without returning row data.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Executed.Add(fetchXml);
            if (Broken) throw new CrmException("CRM down", 503, null, false);
            if (Denied.Any(d => fetchXml.Contains("name=\"" + d + "\""))) throw new CrmException("denied", 403, null, true);
            return Task.FromResult(new ResultTable(new List<string> { "id" }, new List<IReadOnlyList<string>> { new List<string> { "SECRET-ROW-VALUE" } }));
        }
    }

    private readonly MemoryDataPolicyStore _store = new MemoryDataPolicyStore();
    private readonly ProbeExecutor _executor = new ProbeExecutor();
    private readonly List<(string Actor, string Action, string Target, string Detail)> _audit = new List<(string, string, string, string)>();
    private readonly DataPolicyService _policy;
    private readonly DataPolicyAdminService _admin;
    private readonly Caller _caller = Caller.User(Guid.NewGuid());

    public DataPolicyAdminServiceTests()
    {
        _policy = new DataPolicyService(_store, () => new[] { "account", "contact" }, () => true, () => Now, TimeSpan.FromMinutes(5));
        _admin = new DataPolicyAdminService(new FakeMetadataSource(Account, Patient), _store, _policy, _executor,
            (actor, action, target, detail, ct) => { _audit.Add((actor, action, target, detail)); return Task.CompletedTask; }, () => Now);
    }

    /// <summary>
    /// Verifies that the table list shows every table with its state and source.
    /// </summary>
    [Fact]
    public async Task TheTableList_ShowsEveryTable_WithItsStateAndSource()
    {
        var rows = await _admin.ListTablesAsync(null);

        var account = rows.Single(r => r.Entity == "account");
        var patient = rows.Single(r => r.Entity == "new_patient");
        Assert.True(account.Open);
        Assert.Equal("starting list", account.Source);
        Assert.Equal(2, account.AutoClosedFields);
        Assert.False(patient.Open);
        Assert.Equal("closed by default", patient.Source);
        Assert.Null(patient.AutoClosedFields);
    }

    /// <summary>
    /// Verifies that the table list can be searched by name or label.
    /// </summary>
    [Fact]
    public async Task TheTableList_CanBeSearched_ByNameOrLabel()
    {
        Assert.Equal(new[] { "new_patient" }, (await _admin.ListTablesAsync("patient")).Select(r => r.Entity));
        Assert.Equal(new[] { "account" }, (await _admin.ListTablesAsync("ACCO")).Select(r => r.Entity));
    }

    /// <summary>
    /// Verifies that the field list shows state lock and the automatic flag.
    /// </summary>
    [Fact]
    public async Task TheFieldList_ShowsStateLockAndTheAutomaticFlag()
    {
        var fields = (await _admin.ListFieldsAsync("account"))!;

        Assert.False(fields.Single(f => f.Attribute == "new_iban").Open);
        Assert.Equal("closed automatically", fields.Single(f => f.Attribute == "new_iban").State);
        Assert.True(fields.Single(f => f.Attribute == "accountid").Locked);
        Assert.True(fields.Single(f => f.Attribute == "name").Locked);
        Assert.False(fields.Single(f => f.Attribute == "address1_city").Locked);
        Assert.True(fields.Single(f => f.Attribute == "address1_city").Open);
        Assert.Null(await _admin.ListFieldsAsync("does_not_exist"));
    }

    /// <summary>
    /// Verifies that the review list names every field closed automatically in open tables.
    /// </summary>
    [Fact]
    public async Task TheReviewList_NamesEveryFieldThatWasClosedAutomatically_InOpenTables()
    {
        var review = await _admin.ReviewAsync();

        Assert.Equal(new[] { "new_iban", "new_tckimlikno" }, review.Where(r => r.Entity == "account").Select(r => r.Attribute).OrderBy(x => x));
        Assert.DoesNotContain(review, r => r.Entity == "new_patient");
    }

    /// <summary>
    /// Verifies that opening a table stores the rule writes an audit record and takes effect at once.
    /// </summary>
    [Fact]
    public async Task OpeningATable_StoresTheRule_WritesAnAuditRecord_AndTakesEffectAtOnce()
    {
        var result = await _admin.SetTableAsync("CONTOSO\\admin", "new_patient", true, _caller);

        Assert.True(result.Ok);
        Assert.True((await _policy.GetAsync()).IsTableOpen("new_patient"));
        var rule = Assert.Single(await _store.ListAsync());
        Assert.Equal("CONTOSO\\admin", rule.SetBy);
        var entry = Assert.Single(_audit);
        Assert.Equal("datapolicy.table", entry.Action);
        Assert.Equal("new_patient", entry.Target);
        Assert.Contains("closed by default", entry.Detail);
        Assert.Contains("open", entry.Detail);
    }

    /// <summary>
    /// Verifies that a table the CRM cannot read cannot be opened and nothing is stored or audited.
    /// </summary>
    [Fact]
    public async Task ATableTheCrmCannotRead_CannotBeOpened_AndNothingIsStoredOrAudited()
    {
        _executor.Denied.Add("new_patient");

        var result = await _admin.SetTableAsync("a", "new_patient", true, _caller);

        Assert.False(result.Ok);
        Assert.Contains("refused to read", result.Error);
        Assert.Contains("-ReadTables new_patient", result.Error);
        Assert.Empty(await _store.ListAsync());
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that closing a table needs no probe and resetting removes the rule.
    /// </summary>
    [Fact]
    public async Task ClosingATable_NeedsNoProbe_AndResettingRemovesTheRule()
    {
        await _admin.SetTableAsync("a", "account", false, _caller);
        Assert.Empty(_executor.Executed);
        Assert.False((await _policy.GetAsync()).IsTableOpen("account"));

        await _admin.SetTableAsync("a", "account", null, _caller);
        Assert.Empty(await _store.ListAsync());
        Assert.True((await _policy.GetAsync()).IsTableOpen("account"));
        Assert.Equal(2, _audit.Count);
    }

    /// <summary>
    /// Verifies that unknown tables are refused.
    /// </summary>
    [Fact]
    public async Task UnknownTables_AreRefused()
    {
        var result = await _admin.SetTableAsync("a", "no_such_table", true, _caller);

        Assert.False(result.Ok);
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that closing and reopening a field is stored audited and effective.
    /// </summary>
    [Fact]
    public async Task ClosingAndReopeningAField_IsStored_AuditedAndEffective()
    {
        await _admin.SetFieldAsync("a", "account", "address1_city", false);
        Assert.False((await _policy.GetAsync()).IsFieldOpen("account", "address1_city", "City"));

        await _admin.SetFieldAsync("a", "account", "new_iban", true);
        Assert.True((await _policy.GetAsync()).IsFieldOpen("account", "new_iban", "IBAN"));

        Assert.Equal(new[] { "datapolicy.field", "datapolicy.field" }, _audit.Select(e => e.Action));
        Assert.Equal("account.address1_city", _audit[0].Target);
        Assert.DoesNotContain("SECRET", string.Join(" ", _audit.Select(e => e.Detail)));
    }

    /// <summary>
    /// Verifies that the primary id and name are locked and unknown fields and tables are refused.
    /// </summary>
    [Fact]
    public async Task ThePrimaryIdAndNameAreLocked_UnknownFieldsAndTablesAreRefused()
    {
        Assert.False((await _admin.SetFieldAsync("a", "account", "accountid", false)).Ok);
        Assert.False((await _admin.SetFieldAsync("a", "account", "name", false)).Ok);
        Assert.False((await _admin.SetFieldAsync("a", "account", "nope", false)).Ok);
        Assert.False((await _admin.SetFieldAsync("a", "nope_table", "x", false)).Ok);
        Assert.Empty(await _store.ListAsync());
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that the access check reports readable or not and never returns row data.
    /// </summary>
    [Fact]
    public async Task CheckAccess_ReportsReadableOrNot_AndNeverReturnsRowData()
    {
        _executor.Denied.Add("new_patient");

        var ok = await _admin.CheckAccessAsync("account", _caller);
        var denied = await _admin.CheckAccessAsync("new_patient", _caller);

        Assert.Equal("readable", ok.Outcome);
        Assert.Equal("not_readable", denied.Outcome);
        Assert.DoesNotContain("SECRET", ok.Message + denied.Message);
        Assert.All(_executor.Executed, xml => Assert.Contains("top=\"1\"", xml));
    }

    /// <summary>
    /// Verifies that the access check reports an error instead of not readable during a CRM outage.
    /// </summary>
    [Fact]
    public async Task CheckAccess_OnACrmOutage_SaysErrorInsteadOfNotReadable()
    {
        _executor.Broken = true;

        var result = await _admin.CheckAccessAsync("account", _caller);

        Assert.Equal("error", result.Outcome);
    }
}
