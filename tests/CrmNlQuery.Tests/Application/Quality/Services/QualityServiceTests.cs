using System.Text.Json;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Application.Quality.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Infrastructure.Persistence.Memory.Quality;

namespace CrmNlQuery.Tests.Application.Quality.Services;

/// <summary>
/// Holds tests for the acceptance-set service: generation from schema, duplicates and limits, approval, retirement and export.
/// </summary>
public class QualityServiceTests
{
    private sealed class Schema : IMetadataSource
    {
        private readonly EntityInfo _incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo>
            {
                new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
                new AttributeInfo("prioritycode", "Priority", "Picklist", new List<OptionInfo> { new OptionInfo(1, "High"), new OptionInfo(2, "Normal"), new OptionInfo(3, "Low") }),
                new AttributeInfo("statecode", "Status", "State", new List<OptionInfo> { new OptionInfo(0, "Active"), new OptionInfo(1, "Resolved") }),
                new AttributeInfo("estimatedvalue", "Estimated Value", "Money", new List<OptionInfo>()),
            },
            new List<RelationshipInfo>());

        /// <summary>
        /// Test schema source that exposes only the incident table as open.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary>
            {
                new EntitySummary("incident", "incidents", "Case"), new EntitySummary("systemuser", "systemusers", "User"), new EntitySummary("new_arac", "new_aracs", "Araç"),
            });

        /// <summary>
        /// Test schema source that returns entity info for the open table only.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => Task.FromResult<EntityInfo?>(logicalName == "incident" ? _incident : null);

        /// <summary>
        /// Test schema source that ignores a refresh.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Executor : IQueryExecutor
    {
        public int Calls { get; private set; }
        public int? FixedRows { get; set; }

        /// <summary>
        /// Test executor that returns a canned result table.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            if (FixedRows is not null)
            {
                var rows = Enumerable.Range(0, FixedRows.Value).Select(i => (IReadOnlyList<string>)new[] { "row " + i }).ToList();
                return Task.FromResult(new ResultTable(new[] { "Case Title" }, rows));
            }
            static ResultTable Raw(params string[][] rows) => new ResultTable(new[] { "a", "b" }, rows.Select(r => (IReadOnlyList<string>)r).ToList()) { Raw = rows.Select(r => (IReadOnlyList<string>)r).ToList() };
            if (fetchXml.Contains("groupby='true' alias='g'") && fetchXml.Contains("'prioritycode'")) { return Task.FromResult(Raw(new[] { "1", "5" }, new[] { "2", "30" }, new[] { "3", "10" })); }
            if (fetchXml.Contains("groupby='true' alias='g'") && fetchXml.Contains("'statecode'")) { return Task.FromResult(Raw(new[] { "0", "42" }, new[] { "1", "3" })); }
            if (fetchXml.Contains("operator='not-null'")) { return Task.FromResult(Raw(new[] { "40" })); }
            return Task.FromResult(Raw(new[] { "45" }));
        }
    }

    private sealed class Harness
    {
        public readonly MemoryQualityStore Store = new MemoryQualityStore();
        public readonly Executor Executor = new Executor();
        public readonly MemoryHistoryStore History = new MemoryHistoryStore();
        public readonly List<string> Audit = new List<string>();
        public bool Enabled = true;
        public int Max = 500;
        public QualityService Service;

        public Harness()
        {
            Service = new QualityService(Store, new Schema(), Executor, () => Enabled, () => Max,
                (actor, action, target, detail, ct) => { Audit.Add(actor + "|" + action + "|" + target + "|" + detail); return Task.CompletedTask; }, history: History);
        }

        public static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public const string Fetch = "<fetch><entity name='incident'><attribute name='title'/><attribute name='prioritycode'/><order attribute='title'/></entity></fetch>";

        /// <summary>
        /// Test helper that publishes an answer for a question and returns its result id.
        /// </summary>
        public Task<string> Answer(string question = "Show my cases", string fetch = Fetch, int? steps = null, string? kind = null, Guid? owner = null)
            => History.AddAsync(owner ?? Owner, question, "incident", fetch, steps: steps, kind: kind);
    }

    private static readonly QualitySaveRule Rule = new QualitySaveRule("auto", "reference");

    /// <summary>
    /// Verifies that save stores aVerified case with the shown columns and the chosen rule.
    /// </summary>
    [Fact]
    public async Task Save_StoresAVerifiedCase_WithTheShownColumnsAndTheChosenRule()
    {
        var h = new Harness { };
        h.Executor.FixedRows = 3;
        var id = await h.Answer();

        var result = await h.Service.SaveFromResultAsync(Harness.Owner, @"LAB\boss", id, new QualitySaveRule("ordered", "exact"), true, Caller.ForEvaluation(), "tr");

        Assert.Equal(QualitySaveStatus.Saved, result.Status);
        Assert.Equal(3, result.Rows);
        var saved = Assert.Single(await h.Service.ListAsync(new QualityCaseFilter()));
        Assert.Equal(QualityCase.SourceMarked, saved.Source);
        Assert.True(saved.Verified);
        Assert.Equal("tr", saved.Language);
        Assert.Equal("Show my cases", saved.Question);
        Assert.Equal(Harness.Fetch, saved.ReferenceFetch);
        Assert.Contains("custom:title,prioritycode", saved.AnswersJson);
        Assert.Contains("\"order\":\"ordered\"", saved.CompareJson);
        Assert.Contains("\"columns\":\"exact\"", saved.CompareJson);
        Assert.Contains(h.Audit, a => a.StartsWith(@"LAB\boss|quality.mark|"));
        Assert.DoesNotContain(h.Audit, a => a.Contains("Show my cases"));
    }

    /// <summary>
    /// Verifies that save needs the explicit confirmation and aValid rule.
    /// </summary>
    [Fact]
    public async Task Save_NeedsTheExplicitConfirmation_AndAValidRule()
    {
        var h = new Harness();
        h.Executor.FixedRows = 2;
        var id = await h.Answer();

        Assert.Equal(QualitySaveStatus.Invalid, (await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, Rule, false, Caller.ForEvaluation(), "en")).Status);
        Assert.Equal(QualitySaveStatus.Invalid, (await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, new QualitySaveRule("sideways", "reference"), true, Caller.ForEvaluation(), "en")).Status);
        Assert.Empty(await h.Service.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that save refuses empty and truncated results.
    /// </summary>
    [Fact]
    public async Task Save_RefusesEmptyAndTruncatedResults()
    {
        var h = new Harness();
        var id = await h.Answer();

        h.Executor.FixedRows = 0;
        var empty = await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, Rule, true, Caller.ForEvaluation(), "en");
        h.Executor.FixedRows = 500;
        var truncated = await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, Rule, true, Caller.ForEvaluation(), "en");

        Assert.Equal((QualitySaveStatus.TooWeak, "empty"), (empty.Status, empty.Reason));
        Assert.Equal((QualitySaveStatus.TooWeak, "truncated"), (truncated.Status, truncated.Reason));
        Assert.Empty(await h.Service.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that save refuses another users result and non query records.
    /// </summary>
    [Fact]
    public async Task Save_RefusesAnotherUsersResult_AndNonQueryRecords()
    {
        var h = new Harness();
        h.Executor.FixedRows = 2;
        var mine = await h.Answer();
        var summary = await h.Answer(kind: "summary");

        Assert.Equal(QualitySaveStatus.NotFound, (await h.Service.SaveFromResultAsync(Guid.NewGuid(), "a", mine, Rule, true, Caller.ForEvaluation(), "en")).Status);
        Assert.Equal(QualitySaveStatus.NotFound, (await h.Service.SaveFromResultAsync(Harness.Owner, "a", summary, Rule, true, Caller.ForEvaluation(), "en")).Status);
        Assert.Equal(QualitySaveStatus.NotFound, (await h.Service.SaveFromResultAsync(Harness.Owner, "a", "no-such-id", Rule, true, Caller.ForEvaluation(), "en")).Status);
    }

    /// <summary>
    /// Verifies that save refuses chains and edited answers.
    /// </summary>
    [Fact]
    public async Task Save_RefusesChainsAndEditedAnswers()
    {
        var h = new Harness();
        h.Executor.FixedRows = 2;
        var chain = await h.Answer(steps: 2);
        var edited = await h.Answer("Show my cases (edited: removed Priority)");

        var a = await h.Service.SaveFromResultAsync(Harness.Owner, "a", chain, Rule, true, Caller.ForEvaluation(), "en");
        var b = await h.Service.SaveFromResultAsync(Harness.Owner, "a", edited, Rule, true, Caller.ForEvaluation(), "en");

        Assert.Equal((QualitySaveStatus.NotSupported, "chain"), (a.Status, a.Reason));
        Assert.Equal((QualitySaveStatus.NotSupported, "derived"), (b.Status, b.Reason));
    }

    /// <summary>
    /// Verifies that save twice adds only once and aClosed field is refused.
    /// </summary>
    [Fact]
    public async Task Save_TwiceAddsOnlyOnce_AndAClosedFieldIsRefused()
    {
        var h = new Harness();
        h.Executor.FixedRows = 2;
        var id = await h.Answer();
        var closed = await h.Answer("Show secrets", "<fetch><entity name='incident'><attribute name='secretfield'/></entity></fetch>");

        var first = await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, Rule, true, Caller.ForEvaluation(), "en");
        var second = await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, Rule, true, Caller.ForEvaluation(), "en");
        var secret = await h.Service.SaveFromResultAsync(Harness.Owner, "a", closed, Rule, true, Caller.ForEvaluation(), "en");

        Assert.Equal(QualitySaveStatus.Saved, first.Status);
        Assert.Equal(QualitySaveStatus.Duplicate, second.Status);
        Assert.Equal(QualitySaveStatus.DataClosed, secret.Status);
        Assert.Single(await h.Service.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that save stops at the case limit.
    /// </summary>
    [Fact]
    public async Task Save_StopsAtTheCaseLimit()
    {
        var h = new Harness { Max = 0 };
        h.Executor.FixedRows = 2;
        var id = await h.Answer();

        Assert.Equal(QualitySaveStatus.LimitReached, (await h.Service.SaveFromResultAsync(Harness.Owner, "a", id, Rule, true, Caller.ForEvaluation(), "en")).Status);
    }

    /// <summary>
    /// Verifies that generate adds unverified cases read on the callers behalf.
    /// </summary>
    [Fact]
    public async Task Generate_AddsUnverifiedCases_ReadOnTheCallersBehalf()
    {
        var h = new Harness();

        var result = await h.Service.GenerateAsync("incident", "en", @"LAB\boss", Caller.ForEvaluation());

        Assert.True(result.Added >= 5);
        Assert.Equal(0, result.Duplicates);
        var cases = await h.Service.ListAsync(new QualityCaseFilter());
        Assert.All(cases, c => { Assert.Equal(QualityCase.SourceGenerated, c.Source); Assert.False(c.Verified); Assert.Equal("incident", c.Entity); Assert.Equal(@"LAB\boss", c.CreatedBy); });
        Assert.Contains(cases, c => c.Question == "How many Case records are there?");
        Assert.Contains(h.Audit, a => a.StartsWith(@"LAB\boss|quality.generate|incident|"));
    }

    /// <summary>
    /// Verifies that generating twice adds no duplicates.
    /// </summary>
    [Fact]
    public async Task Generating_Twice_AddsNoDuplicates()
    {
        var h = new Harness();
        var first = await h.Service.GenerateAsync("incident", "en", "a", Caller.ForEvaluation());

        var second = await h.Service.GenerateAsync("incident", "en", "a", Caller.ForEvaluation());

        Assert.Equal(0, second.Added);
        Assert.Equal(first.Added, second.Duplicates);
        Assert.Equal(first.Added, (await h.Service.ListAsync(new QualityCaseFilter())).Count);
    }

    /// <summary>
    /// Verifies that aClosed or unknown table generates nothing and never queries the crm.
    /// </summary>
    [Fact]
    public async Task AClosedOrUnknownTable_GeneratesNothing_AndNeverQueriesTheCrm()
    {
        var h = new Harness();

        var result = await h.Service.GenerateAsync("systemuser", "en", "a", Caller.ForEvaluation());

        Assert.Equal("unknown_or_closed", result.Reason);
        Assert.Equal(0, h.Executor.Calls);
        Assert.Empty(await h.Service.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that the case limit stops generation.
    /// </summary>
    [Fact]
    public async Task TheCaseLimit_StopsGeneration()
    {
        var h = new Harness { Max = 3 };

        var result = await h.Service.GenerateAsync("incident", "en", "a", Caller.ForEvaluation());

        Assert.Equal(3, result.Added);
        Assert.Equal("limit_reached", result.Reason);
    }

    /// <summary>
    /// Verifies that candidates are the open business tables not system ones.
    /// </summary>
    [Fact]
    public async Task Candidates_AreTheOpenBusinessTables_NotSystemOnes()
    {
        var h = new Harness();

        var candidates = await h.Service.CandidatesAsync();

        Assert.Contains(candidates, c => c.Entity == "incident");
        Assert.Contains(candidates, c => c.Entity == "new_arac");
        Assert.DoesNotContain(candidates, c => c.Entity == "systemuser");
    }

    /// <summary>
    /// Verifies that approve retire restore delete change the cases and are audited without the question text.
    /// </summary>
    [Fact]
    public async Task Approve_Retire_Restore_Delete_ChangeTheCases_AndAreAudited_WithoutTheQuestionText()
    {
        var h = new Harness();
        await h.Service.GenerateAsync("incident", "en", "a", Caller.ForEvaluation());
        var ids = (await h.Service.ListAsync(new QualityCaseFilter())).Select(c => c.Id).Take(2).ToList();

        Assert.Equal(2, await h.Service.ApproveAsync(ids, "boss"));
        Assert.Equal(2, (await h.Service.ListAsync(new QualityCaseFilter(Verified: true))).Count);
        Assert.Equal(1, await h.Service.RetireAsync(new[] { ids[0] }, true, "boss"));
        Assert.Single(await h.Service.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusRetired)));
        Assert.Equal(1, await h.Service.RetireAsync(new[] { ids[0] }, false, "boss"));
        Assert.Empty(await h.Service.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusRetired)));
        Assert.Equal(1, await h.Service.DeleteAsync(new[] { ids[1] }, "boss"));
        Assert.DoesNotContain(await h.Service.ListAsync(new QualityCaseFilter()), c => c.Id == ids[1]);

        Assert.Contains(h.Audit, a => a.Contains("|quality.approve|"));
        Assert.Contains(h.Audit, a => a.Contains("|quality.retire|"));
        Assert.Contains(h.Audit, a => a.Contains("|quality.restore|"));
        Assert.Contains(h.Audit, a => a.Contains("|quality.delete|"));
        Assert.DoesNotContain(h.Audit, a => a.Contains("How many"));
    }

    /// <summary>
    /// Verifies that export uses the eval case format and skips retired cases.
    /// </summary>
    [Fact]
    public async Task Export_UsesTheEvalCaseFormat_AndSkipsRetiredCases()
    {
        var h = new Harness();
        await h.Service.GenerateAsync("incident", "en", "a", Caller.ForEvaluation());
        var all = await h.Service.ListAsync(new QualityCaseFilter());
        await h.Service.RetireAsync(new[] { all[0].Id }, true, "boss");
        await h.Service.ApproveAsync(new[] { all[1].Id }, "boss");

        var json = JsonDocument.Parse(await h.Service.ExportAsync()).RootElement;

        Assert.Equal(all.Count - 1, json.GetArrayLength());
        var first = json[0];
        Assert.Equal("answer", first.GetProperty("expect").GetProperty("finalKind").GetString());
        Assert.Equal("incident", first.GetProperty("expect").GetProperty("entity").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("expect").GetProperty("reference").GetProperty("fetchXml").GetString()));
        Assert.True(first.GetProperty("generated").GetBoolean());
        Assert.Contains(json.EnumerateArray(), c => c.GetProperty("verified").GetBoolean());
        Assert.Contains("q-", first.GetProperty("id").GetString());
    }

    /// <summary>
    /// Verifies that the memory store keeps IDs unique and says it is not persistent.
    /// </summary>
    [Fact]
    public async Task TheMemoryStore_KeepsIdsUnique_AndSaysItIsNotPersistent()
    {
        var h = new Harness();
        await h.Service.GenerateAsync("incident", "en", "a", Caller.ForEvaluation());

        var ids = (await h.Service.ListAsync(new QualityCaseFilter())).Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.False(h.Service.Persistent);
    }
}
