using System.Text;
using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Glossary;

/// <summary>
/// Tests the business glossary core: validator, prompt, enforcer and matching rules.
/// </summary>
public class GlossaryCoreTests
{
    private sealed class Schema : IMetadataSource
    {
        private readonly EntityInfo _account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo>
            {
                new AttributeInfo("accountid", "Account", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("name", "Account Name", "String", new List<OptionInfo>()),
                new AttributeInfo("numberofemployees", "Employees", "Integer", new List<OptionInfo>()),
                new AttributeInfo("statecode", "Status", "State", new List<OptionInfo> { new OptionInfo(0, "Active"), new OptionInfo(1, "Inactive") }),
                new AttributeInfo("customertypecode", "Type", "Picklist", new List<OptionInfo> { new OptionInfo(1, "Competitor"), new OptionInfo(3, "Customer") }),
                new AttributeInfo("createdon", "Created On", "DateTime", new List<OptionInfo>()),
                new AttributeInfo("tags", "Tags", "MultiSelectPicklist", new List<OptionInfo> { new OptionInfo(1, "A") }),
            }, new List<RelationshipInfo>());

        private readonly EntityInfo _incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo>
            {
                new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
                new AttributeInfo("prioritycode", "Priority", "Picklist", new List<OptionInfo> { new OptionInfo(1, "High"), new OptionInfo(2, "Normal"), new OptionInfo(3, "Low") }),
            }, new List<RelationshipInfo>());

        /// <summary>
        /// Returns the test schema's entity list.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary> { new EntitySummary("account", "accounts", "Account"), new EntitySummary("incident", "incidents", "Case") });

        /// <summary>
        /// Returns the test schema's entity definition, or null when unknown.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
            => Task.FromResult<EntityInfo?>(logicalName == "account" ? _account : logicalName == "incident" ? _incident : null);

        /// <summary>
        /// Does nothing because the fake metadata never changes.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static GlossaryEntry Filter(string term, string entity, params GlossaryCondition[] conditions) => new GlossaryEntry
    {
        Kind = GlossaryKind.Filter, Terms = new[] { new GlossaryTerm(term) }, Entity = entity, Conditions = conditions,
    };

    private static GlossaryCondition Cond(string attribute, string op, params string[] values) => new GlossaryCondition(attribute, op, values);

    private static Task<GlossaryCheck> Check(GlossaryEntry entry) => GlossaryValidator.CheckAsync(entry, new Schema());

    /// <summary>
    /// Verifies that a valid filter and an alias pass.
    /// </summary>
    [Fact]
    public async Task AValidFilterAndAnAlias_Pass()
    {
        Assert.True((await Check(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000"), Cond("statecode", "eq", "0")))).Ok);
        Assert.True((await Check(new GlossaryEntry { Kind = GlossaryKind.Alias, Terms = new[] { new GlossaryTerm("talep") }, Entity = "incident" })).Ok);
    }

    /// <summary>
    /// Verifies that invalid definitions are refused with a reason.
    /// </summary>
    [Theory]
    [InlineData("secret", "numberofemployees", "gt", "1", "entity_unavailable")]
    [InlineData("account", "hidden", "eq", "1", "attribute_unavailable")]
    [InlineData("account", "numberofemployees", "like", "1", "operator_not_allowed")]
    [InlineData("account", "name", "gt", "a", "operator_not_allowed")]
    [InlineData("account", "numberofemployees", "gt", "abc", "value_invalid")]
    [InlineData("account", "customertypecode", "eq", "99", "option_unknown")]
    [InlineData("account", "customertypecode", "eq", "x", "value_invalid")]
    [InlineData("account", "createdon", "gt", "not a date", "value_invalid")]
    [InlineData("account", "tags", "eq", "1", "type_unsupported")]
    [InlineData("account", "numberofemployees", "foo", "1", "operator_not_allowed")]
    public async Task InvalidDefinitions_AreRefused_WithAReason(string entity, string attribute, string op, string value, string reason)
    {
        var result = await Check(Filter("terim", entity, Cond(attribute, op, value)));

        Assert.False(result.Ok);
        Assert.Equal(reason, result.Reason);
    }

    /// <summary>
    /// Verifies that value count rules and term rules are checked.
    /// </summary>
    [Fact]
    public async Task ValueCountRules_AndTermRules_AreChecked()
    {
        Assert.Equal("value_missing", (await Check(Filter("terim", "account", Cond("numberofemployees", "gt")))).Reason);
        Assert.Equal("value_invalid", (await Check(Filter("terim", "account", Cond("numberofemployees", "gt", "1", "2")))).Reason);
        Assert.True((await Check(Filter("terim", "account", Cond("customertypecode", "in", "1", "3")))).Ok);
        Assert.True((await Check(Filter("terim", "account", Cond("name", "not-null")))).Ok);
        Assert.Equal("value_invalid", (await Check(Filter("terim", "account", Cond("name", "not-null", "x")))).Reason);
        Assert.Equal("conditions_missing", (await Check(Filter("terim", "account"))).Reason);
        Assert.Equal("terms_invalid", (await Check(Filter("x", "account", Cond("name", "null")))).Reason);
        Assert.Equal("terms_invalid", (await Check(Filter("terim", "account", Cond("name", "null")) with { Terms = new List<GlossaryTerm>() })).Reason);
    }

    /// <summary>
    /// Verifies that the prompt is empty without hits and lists filters before aliases.
    /// </summary>
    [Fact]
    public void ThePrompt_IsEmptyWithoutHits_AndListsFiltersBeforeAliases()
    {
        var empty = new StringBuilder();
        GlossaryPrompt.AppendForPlan(empty, new List<GlossaryHit>());
        GlossaryPrompt.AppendForAnalysis(empty, null);
        Assert.Equal("", empty.ToString());

        var alias = new GlossaryHit(new GlossaryEntry { Kind = GlossaryKind.Alias, Terms = new[] { new GlossaryTerm("talep") }, Entity = "incident" }, "talep");
        var filter = new GlossaryHit(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")) with { Description = "more than 1000 staff" }, "büyük firma");
        var sb = new StringBuilder();
        GlossaryPrompt.AppendForPlan(sb, new[] { alias, filter });

        var text = sb.ToString();
        Assert.Contains(GlossaryPrompt.Header, text);
        Assert.True(text.IndexOf("require ALL", StringComparison.Ordinal) < text.IndexOf("means incident", StringComparison.Ordinal));
        Assert.Contains("numberofemployees gt 1000", text);
        Assert.Contains("more than 1000 staff", text);

        var analysis = new StringBuilder();
        GlossaryPrompt.AppendForAnalysis(analysis, new[] { alias, filter });
        Assert.Contains("\"talep\" refers to the entity incident", analysis.ToString());
        Assert.DoesNotContain("numberofemployees", analysis.ToString());
    }

    private static GlossaryHit BigFirms() => new GlossaryHit(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")), "büyük firma");

    private static string Fetch(string inner, string root = "account") => $"<fetch><entity name='{root}'><attribute name='name'/>{inner}</entity></fetch>";

    /// <summary>
    /// Verifies that a root condition that is present is applied and a missing one is an error the model can fix.
    /// </summary>
    [Fact]
    public void ARootCondition_ThatIsPresent_IsApplied_AndAMissingOneIsAnErrorTheModelCanFix()
    {
        var applied = GlossaryEnforcer.Evaluate(Fetch("<filter><condition attribute='numberofemployees' operator='gt' value='1000'/></filter>"), new[] { BigFirms() });
        var missing = GlossaryEnforcer.Evaluate(Fetch("<filter><condition attribute='name' operator='like' value='%a%'/></filter>"), new[] { BigFirms() });

        Assert.True(Assert.Single(applied).Applied);
        var finding = Assert.Single(missing);
        Assert.False(finding.Applied);
        Assert.Contains(GlossaryEnforcer.Marker, finding.Error);
        Assert.Contains("numberofemployees gt 1000", finding.Error);
    }

    /// <summary>
    /// Verifies that the value must match numbers as numbers and or filters do not count.
    /// </summary>
    [Fact]
    public void TheValueMustMatch_NumbersAsNumbers_AndOrFiltersDoNotCount()
    {
        Assert.True(Assert.Single(GlossaryEnforcer.Evaluate(Fetch("<filter><condition attribute='numberofemployees' operator='gt' value='1000.0'/></filter>"), new[] { BigFirms() })).Applied);
        Assert.False(Assert.Single(GlossaryEnforcer.Evaluate(Fetch("<filter><condition attribute='numberofemployees' operator='gt' value='500'/></filter>"), new[] { BigFirms() })).Applied);
        Assert.False(Assert.Single(GlossaryEnforcer.Evaluate(Fetch("<filter><condition attribute='numberofemployees' operator='ge' value='1000'/></filter>"), new[] { BigFirms() })).Applied);
        var orFilter = "<filter type='or'><condition attribute='numberofemployees' operator='gt' value='1000'/><condition attribute='name' operator='like' value='%a%'/></filter>";
        Assert.False(Assert.Single(GlossaryEnforcer.Evaluate(Fetch(orFilter), new[] { BigFirms() })).Applied);
    }

    /// <summary>
    /// Verifies that a linked table is a scope too but a condition on another table does not count.
    /// </summary>
    [Fact]
    public void ALinkedTableIsAScopeToo_ButAConditionOnAnotherTableDoesNotCount()
    {
        var linked = "<link-entity name='account' from='accountid' to='parentcustomerid' alias='a'><filter><condition attribute='numberofemployees' operator='gt' value='1000'/></filter></link-entity>";
        var wrongTable = "<link-entity name='incident' from='incidentid' to='x' alias='i'><filter><condition attribute='numberofemployees' operator='gt' value='1000'/></filter></link-entity>";

        Assert.True(Assert.Single(GlossaryEnforcer.Evaluate(Fetch(linked, "contact"), new[] { BigFirms() })).Applied);
        Assert.Empty(GlossaryEnforcer.Evaluate(Fetch(wrongTable, "contact"), new[] { BigFirms() }));
    }

    /// <summary>
    /// Verifies that an option filter with several values is compared as a set and an alias is applied when its table is used.
    /// </summary>
    [Fact]
    public void AnOptionFilterWithSeveralValues_IsComparedAsASet_AndAnAliasIsAppliedWhenItsTableIsUsed()
    {
        var types = new GlossaryHit(Filter("müşteri tipi", "account", Cond("customertypecode", "in", "1", "3")), "müşteri tipi");
        var asChildren = "<filter><condition attribute='customertypecode' operator='in'><value>3</value><value>1</value></condition></filter>";
        var alias = new GlossaryHit(new GlossaryEntry { Kind = GlossaryKind.Alias, Terms = new[] { new GlossaryTerm("talep") }, Entity = "incident" }, "talep");

        Assert.True(Assert.Single(GlossaryEnforcer.Evaluate(Fetch(asChildren), new[] { types })).Applied);
        Assert.True(Assert.Single(GlossaryEnforcer.Evaluate(Fetch("", "incident"), new[] { alias })).Applied);
        Assert.Empty(GlossaryEnforcer.Evaluate(Fetch("", "account"), new[] { alias }));
    }

    private sealed class Harness
    {
        public readonly MemoryGlossaryStore Store = new MemoryGlossaryStore();
        public readonly List<string> Audit = new List<string>();
        public bool Enabled = true;
        public GlossaryService Service;

        public Harness()
        {
            Service = new GlossaryService(Store, new Schema(), () => Enabled, (actor, action, target, detail, ct) => { Audit.Add(actor + "|" + action + "|" + target + "|" + detail); return Task.CompletedTask; });
        }
    }

    /// <summary>
    /// Verifies that add validates refuses a duplicate term and writes audit without anything sensitive.
    /// </summary>
    [Fact]
    public async Task Add_Validates_RefusesADuplicateTerm_AndWritesAuditWithoutAnythingSensitive()
    {
        var h = new Harness();

        var ok = await h.Service.AddAsync(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")), "boss");
        var duplicate = await h.Service.AddAsync(Filter("BÜYÜK FİRMA'lar", "account", Cond("numberofemployees", "gt", "5")), "boss");
        var invalid = await h.Service.AddAsync(Filter("yeni terim", "account", Cond("hidden", "eq", "1")), "boss");

        Assert.Equal(GlossarySaveStatus.Saved, ok.Status);
        Assert.Equal(GlossarySaveStatus.Duplicate, duplicate.Status);
        Assert.Equal(ok.EntryId, duplicate.EntryId);
        Assert.Equal((GlossarySaveStatus.Invalid, "attribute_unavailable"), (invalid.Status, invalid.Reason));
        Assert.Single(await h.Service.ListAsync());
        Assert.Contains(h.Audit, a => a.StartsWith("boss|glossary.create|") && a.Contains("büyük firma"));
    }

    /// <summary>
    /// Verifies that match returns only valid enabled entries and a broken entry leaves the prompt.
    /// </summary>
    [Fact]
    public async Task Match_ReturnsOnlyValidEnabledEntries_AndABrokenEntryLeavesThePrompt()
    {
        var h = new Harness();
        await h.Service.AddAsync(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")), "boss");
        await h.Store.AddAsync(Filter("eski terim", "account", Cond("hidden", "eq", "1")) with { CreatedAt = DateTimeOffset.UtcNow });
        h.Service.Invalidate();

        var hits = await h.Service.MatchAsync("büyük firmaların ve eski terimin listesi");

        Assert.Equal("büyük firma", Assert.Single(hits).Phrase);
    }

    /// <summary>
    /// Verifies that match is empty when the feature is off or there are no entries.
    /// </summary>
    [Fact]
    public async Task Match_IsEmptyWhenTheFeatureIsOff_OrThereAreNoEntries()
    {
        var h = new Harness();
        Assert.Empty(await h.Service.MatchAsync("büyük firma"));
        await h.Service.AddAsync(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")), "boss");
        h.Enabled = false;

        Assert.Empty(await h.Service.MatchAsync("büyük firma"));
    }

    /// <summary>
    /// Verifies that update and delete change the store invalidate and are audited.
    /// </summary>
    [Fact]
    public async Task UpdateAndDelete_ChangeTheStore_Invalidate_AndAreAudited()
    {
        var h = new Harness();
        var saved = await h.Service.AddAsync(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")), "boss");
        Assert.Single(await h.Service.MatchAsync("büyük firma"));

        var entry = (await h.Service.ListAsync()).Single();
        var off = await h.Service.UpdateAsync(entry with { Enabled = false }, "boss");
        Assert.Equal(GlossarySaveStatus.Saved, off.Status);
        Assert.Empty(await h.Service.MatchAsync("büyük firma"));
        Assert.Contains(h.Audit, a => a.Contains("|glossary.disable|"));

        Assert.True(await h.Service.DeleteAsync(saved.EntryId!, "boss"));
        Assert.False(await h.Service.DeleteAsync(saved.EntryId!, "boss"));
        Assert.Equal(GlossarySaveStatus.NotFound, (await h.Service.UpdateAsync(entry, "boss")).Status);
        Assert.Contains(h.Audit, a => a.Contains("|glossary.delete|"));
    }

    /// <summary>
    /// Verifies that the counters record applied and not applied per entry.
    /// </summary>
    [Fact]
    public async Task TheCounters_RecordAppliedAndNotApplied_PerEntry()
    {
        var h = new Harness();
        var saved = await h.Service.AddAsync(Filter("büyük firma", "account", Cond("numberofemployees", "gt", "1000")), "boss");
        var hit = (await h.Service.MatchAsync("büyük firma")).Single();

        await h.Service.RecordAsync(new[] { new GlossaryFinding(hit, true, null), new GlossaryFinding(hit, false, "x"), new GlossaryFinding(hit, false, "x") });

        var stats = Assert.Single(await h.Service.StatsAsync());
        Assert.Equal((saved.EntryId, 1, 2), (stats.EntryId, stats.Applied, stats.NotApplied));
        Assert.NotNull(stats.LastNotAppliedAt);
    }
}
