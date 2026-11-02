using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.DataPolicy;

/// <summary>
/// Tests that the data policy allowlist is enforced throughout the pipeline: validator, default columns and form context cannot use closed data.
/// </summary>
public class PolicyEnforcementTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AttributeInfo A(string name, string label, string type = "String") => new AttributeInfo(name, label, type, new List<OptionInfo>());

    private static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Name"), A("address1_city", "City"), A("new_iban", "IBAN"), A("revenue", "Revenue", "Money") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
        new List<AttributeInfo> { A("contactid", "Contact", "Uniqueidentifier"), A("fullname", "Full Name"), A("parentcustomerid", "Company", "Customer"), A("jobtitle", "Job Title") }, new List<RelationshipInfo>());

    private static readonly EntityInfo Patient = new EntityInfo("new_patient", "new_patients", "Patient", "new_patientid", "new_name",
        new List<AttributeInfo> { A("new_patientid", "Patient", "Uniqueidentifier"), A("new_name", "Name"), A("new_diagnosis", "Diagnosis") }, new List<RelationshipInfo>());

    private sealed class FixedPolicy : IDataPolicy
    {
        public DataPolicySnapshot Snapshot { get; set; }
        public FixedPolicy(DataPolicySnapshot snapshot) { Snapshot = snapshot; }
        /// <summary>
        /// Test fake that returns a fixed data policy snapshot.
        /// </summary>
        public Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(Snapshot);
    }

    private static DataPolicyRule Closed(string entity, string attribute) => new DataPolicyRule("field", entity, attribute, false, "admin", Now);

    private static (IMetadataSource Source, FixedPolicy Policy) Make(params DataPolicyRule[] rules)
    {
        var policy = new FixedPolicy(new DataPolicySnapshot(rules.ToList(), new[] { "account", "contact" }));
        return (new PolicyMetadataSource(new FakeMetadataSource(Account, Contact, Patient), policy), policy);
    }

    private static async Task<ValidationResult> Validate(string fetchXml, params DataPolicyRule[] rules)
        => await new PlanValidator(Make(rules).Source).ValidateFetchXmlAsync(fetchXml);

    /// <summary>
    /// Verifies that a baseline query using only open fields is valid.
    /// </summary>
    [Fact]
    public async Task TheBaselineQuery_WithOnlyOpenFields_IsValid()
    {
        var r = await Validate("<fetch><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"address1_city\" /><filter><condition attribute=\"revenue\" operator=\"gt\" value=\"1\" /></filter><order attribute=\"name\" /></entity></fetch>");

        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Verifies that a closed field is rejected wherever it appears in a query (column, filter and similar positions).
    /// </summary>
    [Theory]
    [InlineData("<attribute name=\"new_iban\" />")]
    [InlineData("<filter><condition attribute=\"new_iban\" operator=\"eq\" value=\"TR00\" /></filter>")]
    [InlineData("<filter><condition attribute=\"new_iban\" operator=\"not-null\" /></filter>")]
    [InlineData("<order attribute=\"new_iban\" />")]
    public async Task AClosedField_InAColumnFilterOrOrder_IsRejected(string inner)
    {
        var r = await Validate("<fetch><entity name=\"account\"><attribute name=\"name\" />" + inner + "</entity></fetch>");

        Assert.False(r.IsValid);
    }

    /// <summary>
    /// Verifies that a closed field is rejected in aggregate positions such as grouping, measures and ordering.
    /// </summary>
    [Theory]
    [InlineData("<attribute name=\"new_iban\" groupby=\"true\" alias=\"g\" /><attribute name=\"accountid\" aggregate=\"count\" alias=\"n\" />")]
    [InlineData("<attribute name=\"name\" groupby=\"true\" alias=\"g\" /><attribute name=\"new_iban\" aggregate=\"count\" alias=\"n\" />")]
    [InlineData("<attribute name=\"name\" groupby=\"true\" alias=\"g\" /><attribute name=\"revenue\" aggregate=\"sum\" alias=\"n\" />")]
    public async Task AClosedField_InGroupingCountOrSum_IsRejected(string inner)
    {
        var r = await Validate("<fetch aggregate=\"true\"><entity name=\"account\">" + inner + "</entity></fetch>", Closed("account", "revenue"));

        Assert.False(r.IsValid);
    }

    /// <summary>
    /// Verifies that a link to a closed table or through a closed link field is rejected.
    /// </summary>
    [Fact]
    public async Task ALinkToAClosedTable_OrThroughAClosedLinkField_IsRejected()
    {
        var closedTable = await Validate("<fetch><entity name=\"account\"><attribute name=\"name\" /><link-entity name=\"new_patient\" from=\"new_patientid\" to=\"accountid\" alias=\"p\"><attribute name=\"new_name\" /></link-entity></entity></fetch>");
        var link = "<fetch><entity name=\"account\"><attribute name=\"name\" /><link-entity name=\"contact\" from=\"parentcustomerid\" to=\"accountid\" alias=\"c\"><attribute name=\"fullname\" /></link-entity></entity></fetch>";

        Assert.False(closedTable.IsValid);
        Assert.True((await Validate(link)).IsValid);
        Assert.False((await Validate(link, Closed("contact", "parentcustomerid"))).IsValid);
    }

    /// <summary>
    /// Verifies that a closed root table is rejected.
    /// </summary>
    [Fact]
    public async Task ARootTableThatIsClosed_IsRejected()
    {
        var r = await Validate("<fetch><entity name=\"new_patient\"><attribute name=\"new_name\" /></entity></fetch>");

        Assert.False(r.IsValid);
    }

    /// <summary>
    /// Verifies that when the policy switch is off the same queries pass.
    /// </summary>
    [Fact]
    public async Task WhenTheSwitchIsOff_TheSameQueriesPass()
    {
        var (source, policy) = Make();
        policy.Snapshot = policy.Snapshot.WithEnabled(false);
        var r = await new PlanValidator(source).ValidateFetchXmlAsync("<fetch><entity name=\"new_patient\"><attribute name=\"new_diagnosis\" /></entity></fetch>");

        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    private sealed class FakeViews : IDefaultViewSource
    {
        private readonly IReadOnlyList<string> _columns;
        public FakeViews(params string[] columns) { _columns = columns; }
        /// <summary>
        /// Test fake that returns default view columns for an entity.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default) => Task.FromResult(_columns);
    }

    private static QuestionAnalysis Analysis(params string[] columns) => new QuestionAnalysis("account", new List<string>(), new List<Mention>(), columns, false);

    /// <summary>
    /// Verifies that the CRM's default view columns lose the closed ones.
    /// </summary>
    [Fact]
    public async Task TheCrmsDefaultViewColumns_LoseTheClosedOnes()
    {
        var account = (await Make().Source.GetEntityAsync("account"))!;

        var decision = await new ColumnPolicy(new FakeViews("name", "new_iban", "address1_city")).DecideAsync(Analysis(), "default", account);

        Assert.Equal(new[] { "name", "address1_city" }, decision.Columns);
    }

    /// <summary>
    /// Verifies that asking for a closed column by name does not resolve it.
    /// </summary>
    [Fact]
    public async Task AskingForAClosedColumnByName_DoesNotResolveIt()
    {
        var account = (await Make().Source.GetEntityAsync("account"))!;

        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis("IBAN"), null, account);

        Assert.Null(decision.Columns);
        Assert.NotNull(decision.Question);
    }

    private sealed class SpyExecutor : IQueryExecutor
    {
        public int Calls { get; private set; }
        /// <summary>
        /// Test fake executor that returns a canned result table.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ResultTable(new List<string> { "new_name" }, new List<IReadOnlyList<string>> { new List<string> { "x" } }));
        }
    }

    /// <summary>
    /// Verifies that the form context of a closed table is ignored and nothing is read.
    /// </summary>
    [Fact]
    public async Task TheFormContext_OfAClosedTable_IsIgnored_AndNothingIsRead()
    {
        var executor = new SpyExecutor();
        var resolver = new RecordContextResolver(Make().Source, executor);

        var resolved = await resolver.ResolveAsync("new_patient", Guid.NewGuid().ToString(), Caller.User(Guid.NewGuid()));

        Assert.Null(resolved);
        Assert.Equal(0, executor.Calls);
    }
}
