using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Exceptions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.DataPolicy.Services;

/// <summary>
/// Tests that a stored query is checked against the data policy before re-running so later closures block old queries.
/// </summary>
public class StoredQueryCheckTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AttributeInfo A(string name, string label, string type = "String") => new AttributeInfo(name, label, type, new List<OptionInfo>());

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Name"), A("address1_city", "City"), A("new_iban", "IBAN") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
        new List<AttributeInfo> { A("contactid", "Contact", "Uniqueidentifier"), A("fullname", "Full Name"), A("parentcustomerid", "Company", "Customer"), A("jobtitle", "Job Title") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Patient = new EntityInfo("new_patient", "new_patients", "Patient", "new_patientid", "new_name",
        new List<AttributeInfo> { A("new_patientid", "Patient", "Uniqueidentifier"), A("new_name", "Name") }, new List<RelationshipInfo>());

    private sealed class FixedPolicy : IDataPolicy
    {
        private readonly DataPolicySnapshot _snapshot;
        public FixedPolicy(DataPolicySnapshot snapshot) { _snapshot = snapshot; }
        /// <summary>
        /// Returns the data policy snapshot used by the test.
        /// </summary>
        public Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
    }

    private static IMetadataSource Filtered(params DataPolicyRule[] rules)
        => new PolicyMetadataSource(new FakeMetadataSource(Account, Contact, Patient), new FixedPolicy(new DataPolicySnapshot(rules.ToList(), new[] { "account", "contact" })));

    private static DataPolicyRule Closed(string entity, string attribute) => new DataPolicyRule("field", entity, attribute, false, "admin", Now);

    private static Task<bool> Usable(string fetchXml, params DataPolicyRule[] rules) => StoredQueryCheck.IsUsableAsync(fetchXml, Filtered(rules));

    /// <summary>
    /// Verifies that an ordinary stored query is usable.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryStoredQuery_IsUsable()
    {
        Assert.True(await Usable("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"accountid\" alias=\"_rowid\" />" +
            "<filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter><order attribute=\"name\" /></entity></fetch>"));
    }

    /// <summary>
    /// Verifies that a stored query using a closed or sensitive attribute is judged unusable.
    /// </summary>
    [Theory]
    [InlineData("<attribute name=\"new_iban\" />")]
    [InlineData("<filter><condition attribute=\"new_iban\" operator=\"eq\" value=\"x\" /></filter>")]
    [InlineData("<order attribute=\"new_iban\" />")]
    [InlineData("<attribute name=\"new_iban\" groupby=\"true\" alias=\"g\" />")]
    [InlineData("<attribute name=\"new_iban\" aggregate=\"count\" alias=\"n\" />")]
    public async Task AClosedFieldAnywhereInTheQuery_MakesItUnusable(string inner)
    {
        Assert.False(await Usable("<fetch><entity name=\"account\"><attribute name=\"name\" />" + inner + "</entity></fetch>"));
    }

    /// <summary>
    /// Verifies that a field the admin closed later makes an old query unusable, but only for that field.
    /// </summary>
    [Fact]
    public async Task AFieldTheAdminClosedLater_MakesAnOldQueryUnusable_ButOnlyThatField()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"address1_city\" /></entity></fetch>";

        Assert.True(await Usable(xml));
        Assert.False(await Usable(xml, Closed("account", "address1_city")));
    }

    /// <summary>
    /// Verifies that a closed table as root or as a link makes the query unusable.
    /// </summary>
    [Fact]
    public async Task AClosedTable_AsRootOrAsALink_MakesItUnusable()
    {
        Assert.False(await Usable("<fetch><entity name=\"new_patient\"><attribute name=\"new_name\" /></entity></fetch>"));
        Assert.False(await Usable("<fetch><entity name=\"account\"><attribute name=\"name\" /><link-entity name=\"new_patient\" from=\"new_patientid\" to=\"accountid\" alias=\"p\" /></entity></fetch>"));
    }

    /// <summary>
    /// Verifies that linked entity attributes are checked against the linked table.
    /// </summary>
    [Fact]
    public async Task LinkedEntityAttributes_AreCheckedAgainstTheLinkedTable()
    {
        var open = "<fetch><entity name=\"account\"><attribute name=\"name\" /><link-entity name=\"contact\" from=\"parentcustomerid\" to=\"accountid\" alias=\"c\">" +
                   "<attribute name=\"fullname\" /><filter><condition attribute=\"jobtitle\" operator=\"eq\" value=\"CEO\" /></filter></link-entity></entity></fetch>";

        Assert.True(await Usable(open));
        Assert.False(await Usable(open, Closed("contact", "jobtitle")));
        Assert.False(await Usable(open, Closed("contact", "parentcustomerid")));
    }

    /// <summary>
    /// Verifies that a root condition on a linked alias is checked against the linked table.
    /// </summary>
    [Fact]
    public async Task ARootConditionOnALinkedAlias_IsCheckedAgainstTheLinkedTable()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><link-entity name=\"contact\" from=\"parentcustomerid\" to=\"accountid\" alias=\"c\" link-type=\"outer\" />" +
                  "<filter><condition entityname=\"c\" attribute=\"jobtitle\" operator=\"null\" /></filter></entity></fetch>";

        Assert.True(await Usable(xml));
        Assert.False(await Usable(xml, Closed("contact", "jobtitle")));
    }

    /// <summary>
    /// Verifies that empty, malformed or incomplete FetchXML inputs are judged unusable.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not xml <<<")]
    [InlineData("<fetch />")]
    [InlineData("<fetch><entity name=\"unknown_table\" /></fetch>")]
    public async Task BrokenOrUnknownInput_IsUnusable(string xml)
    {
        Assert.False(await Usable(xml));
    }

    private sealed class SpyExecutor : IQueryExecutor
    {
        public int Calls { get; private set; }
        /// <summary>
        /// Executes a query against the fake executor used by the test.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()));
        }
    }

    /// <summary>
    /// Verifies that a stored result touching now-closed data is not rerun and the executor is never called.
    /// </summary>
    [Fact]
    public async Task AStoredResult_ThatTouchesNowClosedData_IsNotRerun_AndTheExecutorIsNeverCalled()
    {
        var store = new MemoryHistoryStore();
        var executor = new SpyExecutor();
        var results = new ResultService(store, Filtered(Closed("account", "address1_city")), executor);
        var me = Guid.NewGuid();
        var id = await store.AddAsync(me, "cities", "account", "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"address1_city\" /></entity></fetch>");

        await Assert.ThrowsAsync<DataPolicyException>(() => results.OpenAsync(id, me));

        Assert.Equal(0, executor.Calls);
    }

    /// <summary>
    /// Verifies that a stored result touching only open data still runs.
    /// </summary>
    [Fact]
    public async Task AStoredResult_ThatOnlyTouchesOpenData_StillRuns()
    {
        var store = new MemoryHistoryStore();
        var executor = new SpyExecutor();
        var results = new ResultService(store, Filtered(), executor);
        var me = Guid.NewGuid();
        var id = await store.AddAsync(me, "names", "account", "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>");

        var outcome = await results.OpenAsync(id, me);

        Assert.NotNull(outcome);
        Assert.Equal(1, executor.Calls);
    }
}
