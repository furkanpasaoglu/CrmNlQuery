using System.Xml.Linq;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Drafts.Services;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Drafts.Services;

/// <summary>
/// Tests the email draft service: placeholder values are read as the caller and never sent to the model, and only placeholders with a value are offered.
/// </summary>
public class DraftServiceTests
{
    internal static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AttributeInfo A(string name, string type = "String") => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static RelationshipInfo ToAccount(string table, string attribute) => new RelationshipInfo("rel_" + attribute, table, attribute, "account", "accountid");

    private static EntityInfo Account(bool primaryContact = true) => new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name"), A("createdon", "DateTime") }.Concat(primaryContact ? new[] { A("primarycontactid", "Lookup") } : Array.Empty<AttributeInfo>()).ToList(),
        new List<RelationshipInfo>());

    private static EntityInfo Contact(bool email = true) => new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
        new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname"), A("firstname"), A("parentcustomerid", "Lookup") }.Concat(email ? new[] { A("emailaddress1") } : Array.Empty<AttributeInfo>()).ToList(),
        new List<RelationshipInfo>());

    private static EntityInfo Opportunity() => new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name"), A("customerid", "Lookup"), A("statecode"), A("estimatedvalue", "Money"), A("estimatedclosedate", "DateTime"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("opportunity", "customerid") });

    private static EntityInfo Incident() => new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
        new List<AttributeInfo> { A("incidentid", "Uniqueidentifier"), A("title"), A("customerid", "Lookup"), A("statecode"), A("prioritycode", "Picklist"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("incident", "customerid") });

    private static EntityInfo Contract() => new EntityInfo("contract", "contracts", "Contract", "contractid", "title",
        new List<AttributeInfo> { A("contractid", "Uniqueidentifier"), A("title"), A("customerid", "Lookup"), A("statecode"), A("expireson", "DateTime") },
        new List<RelationshipInfo> { ToAccount("contract", "customerid") });

    private static EntityInfo Activity() => new EntityInfo("activitypointer", "activitypointers", "Activity", "activityid", "subject",
        new List<AttributeInfo> { A("activityid", "Uniqueidentifier"), A("subject"), A("regardingobjectid", "Lookup"), A("activitytypecode"), A("actualend", "DateTime"), A("scheduledend", "DateTime"), A("statecode"), A("modifiedon", "DateTime"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("activitypointer", "regardingobjectid") });

    /// <summary>
    /// Test fake default view source for the draft tests.
    /// </summary>
    internal sealed class Views : IDefaultViewSource
    {
        /// <summary>
        /// Test fake that returns default view columns for an entity.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(new[] { "name" });
    }

    /// <summary>
    /// Test fake query executor returning canned answers per table, with an option for zero open opportunities.
    /// </summary>
    internal sealed class Executor : IQueryExecutor
    {
        public readonly List<Caller> Callers = new List<Caller>();
        public readonly List<XDocument> Fetches = new List<XDocument>();
        public bool ZeroOpportunities;
        public bool DenyContacts;

        /// <summary>
        /// Test fake that returns a canned result table for a query.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            lock (Callers) { Callers.Add(caller); }
            var fetch = XDocument.Parse(fetchXml).Root!;
            lock (Fetches) { Fetches.Add(fetch.Document!); }
            if (entitySetName == "contacts")
            {
                if (DenyContacts) { throw new CrmException("denied", isPermissionDenied: true); }
                var cells = new Dictionary<string, string> { ["firstname"] = "Ayse", ["fullname"] = "Ayse Demir", ["emailaddress1"] = "ayse.demir@brightwave.example", ["parentcustomerid"] = "Brightwave Appliances Ltd" };
                var columns = fetch.Element("entity")!.Elements("attribute").Select(a => (string)a.Attribute("name")!).ToList();
                return Task.FromResult(new ResultTable(columns, new List<IReadOnlyList<string>> { columns.Select(c => cells.GetValueOrDefault(c, "")).ToList() }));
            }
            if ((string?)fetch.Attribute("aggregate") == "true")
            {
                var grouped = fetch.Descendants("attribute").Any(a => (string?)a.Attribute("groupby") == "true");
                if (grouped) { return Task.FromResult(new ResultTable(new[] { "p", "n" }, new List<IReadOnlyList<string>> { new[] { "High", "2" } })); }
                var hasSum = fetch.Descendants("attribute").Any(a => (string?)a.Attribute("alias") == "s");
                var count = entitySetName == "opportunities" && ZeroOpportunities ? "0" : entitySetName == "incidents" ? "2" : "3";
                return Task.FromResult(hasSum
                    ? new ResultTable(new[] { "n", "s" }, new List<IReadOnlyList<string>> { new[] { count, "166000" } })
                    : new ResultTable(new[] { "n" }, new List<IReadOnlyList<string>> { new[] { count } }));
            }
            var entity = fetch.Element("entity")!;
            var names = entity.Elements("attribute").Select(a => (string?)a.Attribute("alias") ?? (string)a.Attribute("name")!).Where(c => c != "_rowid").ToList();
            var row = names.Select(c => c == "expireson" ? "2026-11-15" : c == "actualend" ? "2026-10-01" : c + "-value").ToList();
            return Task.FromResult(new ResultTable(names, new List<IReadOnlyList<string>> { row }, new List<string> { Guid.NewGuid().ToString() }));
        }
    }

    private static readonly Caller Alice = Caller.User(Guid.NewGuid());

    private static (DraftService Service, Executor Executor, FakeLlm Llm) Build(string[] replies, EntityInfo[] tables, Func<Caller, CancellationToken, Task<string?>>? sender = null, Action<Executor>? tune = null)
    {
        var metadata = new FakeMetadataSource(tables);
        var executor = new Executor();
        tune?.Invoke(executor);
        var summary = new SummaryBuilder(metadata, executor, new Views(), () => TimeSpan.FromSeconds(5), () => new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
        var reader = new DraftValueReader(metadata, executor, summary, () => new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
        var llm = new FakeLlm(replies);
        return (new DraftService(reader, new DraftComposer(llm), sender), executor, llm);
    }

    /// <summary>
    /// Test helper that builds the full set of entity metadata used by draft tests.
    /// </summary>
    internal static EntityInfo[] Everything() => new[] { Account(), Contact(), Opportunity(), Incident(), Contract(), Activity() };

    private const string FullReply = """
    {"subject":"Following up: {{customer_name}}","body":"Dear {{contact_first_name}},\n\n{{customer_name}} has {{open_opportunity_count}} open opportunities worth {{open_opportunity_total}} and {{open_case_count}} open cases. The next contract ends on {{nearest_contract_end}}; we last spoke on {{last_activity_date}}.\n\nBest regards,\n{{sender_name}}\n{{today}}"}
    """;

    /// <summary>
    /// Verifies that the template is filled by code and the model never sees any record value.
    /// </summary>
    [Fact]
    public async Task TheTemplateIsFilledByCode_AndTheModelNeverSeesAnyRecordValue()
    {
        var (service, _, llm) = Build(new[] { FullReply }, Everything(), (_, _) => Task.FromResult<string?>("Deniz Arslan"));

        var outcome = await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write a follow-up email to this customer");

        var draft = Assert.IsType<DraftOutcome>(outcome);
        Assert.Equal("Following up: Brightwave Appliances Ltd", draft.Subject);
        Assert.Contains("Dear Ayse,", draft.Body);
        Assert.Contains("3 open opportunities worth 166000", draft.Body);
        Assert.Contains("2 open cases", draft.Body);
        Assert.Contains("ends on 2026-11-15", draft.Body);
        Assert.Contains("last spoke on 2026-10-01", draft.Body);
        Assert.Contains("Deniz Arslan", draft.Body);
        Assert.Contains("2026-10-23", draft.Body);
        Assert.Empty(draft.Unresolved);
        Assert.Equal("ayse.demir@brightwave.example", draft.To);
        Assert.Equal("{{customer_name}}", draft.TemplateSubject.Substring(draft.TemplateSubject.IndexOf('{')));

        var sent = llm.Calls[0].System + llm.Calls[0].User;
        foreach (var secret in new[] { "Ayse", "Demir", "166000", "ayse.demir", "Brightwave", "Deniz", "2026-11-15", "2026-10-01" })
        {
            Assert.DoesNotContain(secret, sent);
        }
    }

    /// <summary>
    /// Verifies that only placeholders with a value are offered, so a closed or missing table offers nothing.
    /// </summary>
    [Fact]
    public async Task OnlyPlaceholdersThatHaveAValue_AreOffered_SoATableThatIsClosedOrMissingOffersNothing()
    {
        var (service, _, llm) = Build(new[] { """{"subject":"Hello","body":"Dear {{contact_first_name}}, best regards {{sender_name}}"}""" },
            new[] { Account(), Contact(), Opportunity(), Activity() });

        await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email");

        var offered = llm.Calls[0].User;
        Assert.Contains("{{open_opportunity_count}}", offered);
        Assert.DoesNotContain("{{open_case_count}}", offered);
        Assert.DoesNotContain("{{nearest_contract_end}}", offered);
    }

    /// <summary>
    /// Verifies that a placeholder the model uses without being offered is rejected and retried.
    /// </summary>
    [Fact]
    public async Task IfTheModelUsesAPlaceholderThatWasNotOffered_ItIsRejectedAndRetried()
    {
        var (service, _, llm) = Build(new[]
        {
            """{"subject":"Hello","body":"You have {{open_case_count}} open cases. {{sender_name}}"}""",
            """{"subject":"Hello","body":"Dear {{contact_first_name}}, {{sender_name}}"}""",
        }, new[] { Account(), Contact() });

        var outcome = await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email");

        Assert.IsType<DraftOutcome>(outcome);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("open_case_count", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that zero open opportunities offers no opportunity placeholder.
    /// </summary>
    [Fact]
    public async Task ZeroOpenOpportunities_OffersNoOpportunityPlaceholder()
    {
        var (service, _, llm) = Build(new[] { """{"subject":"Hi","body":"Dear {{contact_first_name}}"}""" }, Everything(), tune: e => e.ZeroOpportunities = true);

        await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email");

        Assert.DoesNotContain("open_opportunity", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that a closed contact email field means no recipient but the draft still works.
    /// </summary>
    [Fact]
    public async Task WhenTheContactEmailFieldIsClosed_ThereIsNoRecipient_ButTheDraftStillWorks()
    {
        var (service, _, _) = Build(new[] { """{"subject":"Hi","body":"Dear {{contact_first_name}}"}""" }, new[] { Account(), Contact(email: false) });

        var draft = Assert.IsType<DraftOutcome>(await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email"));

        Assert.Null(draft.To);
        Assert.Contains("Ayse", draft.Body);
    }

    /// <summary>
    /// Verifies that when the CRM refuses the contact read there is no contact information and no placeholder for it.
    /// </summary>
    [Fact]
    public async Task WhenTheCrmRefusesTheContactRead_ThereIsNoContactInformation_AndNoPlaceholderForIt()
    {
        var (service, _, llm) = Build(new[] { """{"subject":"Hi","body":"Dear customer, best regards {{sender_name}}"}""" }, Everything(), tune: e => e.DenyContacts = true);

        var draft = Assert.IsType<DraftOutcome>(await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email"));

        Assert.Null(draft.To);
        Assert.DoesNotContain("{{contact_first_name}}", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that an unknown sender name stays a visible marker in the draft.
    /// </summary>
    [Fact]
    public async Task ASenderNameThatIsNotKnown_StaysAVisibleMarker()
    {
        var (service, _, _) = Build(new[] { """{"subject":"Hi","body":"Regards, {{sender_name}}"}""" }, new[] { Account(), Contact() });

        var draft = Assert.IsType<DraftOutcome>(await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email"));

        Assert.Contains("[your name]", draft.Body);
        Assert.Equal(new[] { "[your name]" }, draft.Unresolved);
    }

    /// <summary>
    /// Verifies that for a contact the contact itself is the recipient and the parent is the customer.
    /// </summary>
    [Fact]
    public async Task ForAContact_TheContactItselfIsTheRecipient_AndTheParentIsTheCustomer()
    {
        var (service, _, _) = Build(new[] { """{"subject":"For {{customer_name}}","body":"Dear {{contact_full_name}}"}""" }, Everything());

        var draft = Assert.IsType<DraftOutcome>(await service.BuildAsync(Alice, "contact", Guid.NewGuid(), "Ayse Demir", "write an email"));

        Assert.Equal("For Brightwave Appliances Ltd", draft.Subject);
        Assert.Equal("Dear Ayse Demir", draft.Body);
        Assert.Equal("ayse.demir@brightwave.example", draft.To);
    }

    /// <summary>
    /// Verifies that a model that keeps inventing facts yields a fixed failure instead of a bad draft.
    /// </summary>
    [Fact]
    public async Task AModelThatKeepsInventingFacts_GivesAFixedFailure_NotABadDraft()
    {
        var bad = """{"subject":"Hi","body":"As we discussed 2 weeks ago..."}""";
        var (service, _, llm) = Build(new[] { bad, bad, bad }, new[] { Account(), Contact() });

        var outcome = await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email");

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("draft_invalid", failure.Kind);
        Assert.Equal(UserMessages.DraftFailed, failure.Message);
        Assert.Equal(3, llm.Calls.Count);
    }

    /// <summary>
    /// Verifies that every link-entity in the draft queries has an alias because the result table builder requires one.
    /// </summary>
    [Fact]
    public async Task EveryLinkEntityInTheQueries_HasAnAlias_BecauseTheResultTableBuilderRequiresOne()
    {
        var (service, executor, _) = Build(new[] { """{"subject":"Hi","body":"Dear {{contact_first_name}}"}""" }, Everything());

        await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email");

        var links = executor.Fetches.SelectMany(f => f.Descendants("link-entity")).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, l => Assert.False(string.IsNullOrEmpty((string?)l.Attribute("alias"))));
    }

    /// <summary>
    /// Verifies that every draft read runs as the caller.
    /// </summary>
    [Fact]
    public async Task EveryReadRunsAsTheCaller()
    {
        var (service, executor, _) = Build(new[] { """{"subject":"Hi","body":"Dear {{contact_first_name}}"}""" }, Everything());

        await service.BuildAsync(Alice, "account", AccountId, "Brightwave Appliances Ltd", "write an email");

        Assert.NotEmpty(executor.Callers);
        Assert.All(executor.Callers, c => Assert.Equal(Alice, c));
    }
}
