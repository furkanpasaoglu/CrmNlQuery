using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Application.Create.Models;
using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Create;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Create.Services;

/// <summary>
/// Unit tests for the record creation service: asking for missing fields, previewing, confirming, cancelling and its safety checks.
/// </summary>
public class CreateServiceTests
{
    private static readonly Caller Ayse = Caller.User(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Caller Mehmet = Caller.User(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));

    private sealed class ScriptedLlm : ILlmClient
    {
        public Queue<string> Replies { get; } = new Queue<string>();
        public int Calls { get; private set; }
        /// <summary>
        /// Returns scripted model output for the test instead of calling a real model.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Replies.Dequeue());
        }
    }

    private sealed class FakeSchemas : ICreateSchemaSource
    {
        /// <summary>
        /// Returns a canned schema for the requested entity in the test.
        /// </summary>
        public Task<CreateSchema?> GetAsync(string entity, CancellationToken ct = default)
            => Task.FromResult(entity == "account" ? CreateValidatorTests.AccountSchema() : null);
    }

    private sealed class FakeWriter : IRecordWriter
    {
        public List<IReadOnlyDictionary<string, object?>> Written { get; } = new List<IReadOnlyDictionary<string, object?>>();
        public bool Deny { get; set; }
        /// <summary>
        /// Pretends to create a record and returns a new id without touching CRM.
        /// </summary>
        public Task<Guid> CreateAsync(CreateSchema schema, IReadOnlyDictionary<string, object?> payload, Caller caller, CancellationToken ct = default)
        {
            if (Deny)
            {
                throw new CrmException("missing prvCreateAccount", 403, isPermissionDenied: true);
            }
            Written.Add(payload);
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class FakeSearcher : IRecordSearcher
    {
        /// <summary>
        /// Returns canned record matches for a name search in the test.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(Array.Empty<RecordMatch>());
    }

    private sealed class FakeMetadata : IMetadataSource
    {
        /// <summary>
        /// Returns the test schema's entity list.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntitySummary>>(Array.Empty<EntitySummary>());
        /// <summary>
        /// Returns the test schema's entity definition, or null when unknown.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => Task.FromResult<EntityInfo?>(null);
        /// <summary>
        /// Does nothing because the fake metadata never changes.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeExecutor : IQueryExecutor
    {
        public List<string> Existing { get; } = new List<string>();
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(new ResultTable(new[] { "name" }, Existing.Select(n => (IReadOnlyList<string>)new[] { n }).ToList()));
    }

    private sealed class Rig
    {
        public ScriptedLlm Llm { get; } = new ScriptedLlm();
        public FakeWriter Writer { get; } = new FakeWriter();
        public FakeExecutor Executor { get; } = new FakeExecutor();
        public MemoryCreateDraftStore Store { get; } = new MemoryCreateDraftStore();
        public List<AuditEntry> Audit { get; } = new List<AuditEntry>();
        public bool Enabled { get; set; } = true;
        public int MaxRows { get; set; } = 50;
        public int PerDay { get; set; } = 200;
        public CreateService Service { get; }

        public Rig()
        {
            Service = new CreateService(new FakeSchemas(), new CreateExtractor(Llm), new FakeSearcher(), new FakeMetadata(), Writer, Executor, Store, null,
                (entry, ct) => { Audit.Add(entry); return Task.CompletedTask; },
                () => Enabled, () => new[] { "account" }, () => MaxRows, () => PerDay);
        }
    }

    private static string Extraction(string entity, params (string, string)[][] records)
        => "{\"entity\":\"" + entity + "\",\"records\":[" + string.Join(",", records.Select(r => "{\"values\":[" + string.Join(",", r.Select(p => "{\"field\":\"" + p.Item1 + "\",\"value\":\"" + p.Item2 + "\"}")) + "]}")) + "]}";

    /// <summary>
    /// Verifies that when the feature is off or the question is not a create request nothing happens.
    /// </summary>
    [Fact]
    public async Task WhenTheFeatureIsOff_OrTheQuestionIsNotACreateRequest_NothingHappens()
    {
        var rig = new Rig { Enabled = false };
        Assert.Null(await rig.Service.HandleAsync("hesap oluştur Alpine", null, Ayse, true));

        rig.Enabled = true;
        Assert.Null(await rig.Service.HandleAsync("Londra'daki hesapları getir", null, Ayse, true));
        Assert.Equal(0, rig.Llm.Calls);
    }

    /// <summary>
    /// Verifies that without permission a create request is refused and the model is not called.
    /// </summary>
    [Fact]
    public async Task WithoutPermission_ACreateRequestIsRefused_AndTheModelIsNotCalled()
    {
        var rig = new Rig();

        var outcome = await rig.Service.HandleAsync("Alpine adında hesap oluştur", null, Ayse, false);

        Assert.Equal("create_forbidden", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Equal(0, rig.Llm.Calls);
    }

    /// <summary>
    /// Verifies that a complete request gives a preview and writes nothing.
    /// </summary>
    [Fact]
    public async Task ACompleteRequest_GivesAPreview_AndWritesNothing()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Deniz Arslan"), ("telephone1", "0555 111 22 33") }));

        var outcome = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("Deniz Arslan adında hesap oluştur, telefon 0555 111 22 33", null, Ayse, true));

        Assert.Equal("preview", outcome.Kind);
        Assert.NotNull(outcome.DraftId);
        var row = Assert.Single(outcome.Rows);
        Assert.Equal("ready", row.Status);
        Assert.Equal("Deniz Arslan", row.Name);
        Assert.Empty(rig.Writer.Written);
    }

    /// <summary>
    /// Verifies that a missing required field is asked then the reply completes the draft.
    /// </summary>
    [Fact]
    public async Task AMissingRequiredField_IsAsked_ThenTheReplyCompletesTheDraft()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("account"));
        var ask = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("hesap oluştur", null, Ayse, true));
        Assert.Equal("ask", ask.Kind);
        Assert.Equal("missing_required", Assert.Single(Assert.Single(ask.Rows).Problems).Code);

        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Alpine") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("adı Alpine", Guid.Parse(ask.DraftId!), Ayse, true));

        Assert.Equal("preview", preview.Kind);
        Assert.Equal("Alpine", Assert.Single(preview.Rows).Name);
        Assert.Empty(rig.Writer.Written);
    }

    /// <summary>
    /// Verifies that a reply that contains no values is treated as a new question.
    /// </summary>
    [Fact]
    public async Task AReplyThatContainsNoValues_IsTreatedAsANewQuestion()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("account"));
        var ask = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("hesap oluştur", null, Ayse, true));

        rig.Llm.Replies.Enqueue(Extraction("account"));
        var next = await rig.Service.HandleAsync("Londra'daki hesapları göster", Guid.Parse(ask.DraftId!), Ayse, true);

        Assert.Null(next);
    }

    /// <summary>
    /// Verifies that confirm writes with the typed payload audits and the draft is spent.
    /// </summary>
    [Fact]
    public async Task Confirm_WritesWithTheTypedPayload_Audits_AndTheDraftIsSpent()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Alpine"), ("numberofemployees", "40") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("hesap oluştur Alpine, 40 çalışan", null, Ayse, true));
        var id = Guid.Parse(preview.DraftId!);

        var done = Assert.IsType<CreateOutcome>(await rig.Service.ConfirmAsync(id, Ayse, @"contoso\ayse", false, true));

        Assert.Equal("done", done.Kind);
        var row = Assert.Single(done.Rows);
        Assert.Equal("created", row.Status);
        Assert.NotNull(row.RecordId);
        var written = Assert.Single(rig.Writer.Written);
        Assert.Equal("Alpine", written["name"]);
        Assert.Equal(40L, written["numberofemployees"]);
        var audit = Assert.Single(rig.Audit);
        Assert.Equal("record.create", audit.Action);
        Assert.Equal(@"contoso\ayse", audit.Actor);
        Assert.DoesNotContain("Alpine", audit.Detail);

        var again = await rig.Service.ConfirmAsync(id, Ayse, @"contoso\ayse", false, true);
        Assert.Equal("create_gone", Assert.IsType<FailureOutcome>(again).Kind);
        Assert.Single(rig.Writer.Written);
    }

    /// <summary>
    /// Verifies that a draft belongs to its owner.
    /// </summary>
    [Fact]
    public async Task ADraftBelongsToItsOwner()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Alpine") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("hesap oluştur Alpine", null, Ayse, true));

        var stolen = await rig.Service.ConfirmAsync(Guid.Parse(preview.DraftId!), Mehmet, @"contoso\mehmet", false, true);

        Assert.Equal("create_gone", Assert.IsType<FailureOutcome>(stolen).Kind);
        Assert.Empty(rig.Writer.Written);
    }

    /// <summary>
    /// Verifies that a duplicate name blocks confirm until the user allows it.
    /// </summary>
    [Fact]
    public async Task ADuplicateName_BlocksConfirm_UntilTheUserAllowsIt()
    {
        var rig = new Rig();
        rig.Executor.Existing.Add("Alpine");
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Alpine") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("hesap oluştur Alpine", null, Ayse, true));
        Assert.Equal("Alpine", Assert.Single(Assert.Single(preview.Rows).Duplicates));
        var id = Guid.Parse(preview.DraftId!);

        var blocked = Assert.IsType<CreateOutcome>(await rig.Service.ConfirmAsync(id, Ayse, "a", false, true));
        Assert.Equal("preview", blocked.Kind);
        Assert.Empty(rig.Writer.Written);

        var done = Assert.IsType<CreateOutcome>(await rig.Service.ConfirmAsync(id, Ayse, "a", true, true));
        Assert.Equal("done", done.Kind);
        Assert.Single(rig.Writer.Written);
    }

    /// <summary>
    /// Verifies that a CRM permission error is reported per row not thrown.
    /// </summary>
    [Fact]
    public async Task ACrmPermissionError_IsReportedPerRow_NotThrown()
    {
        var rig = new Rig();
        rig.Writer.Deny = true;
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Alpine") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("hesap oluştur Alpine", null, Ayse, true));

        var done = Assert.IsType<CreateOutcome>(await rig.Service.ConfirmAsync(Guid.Parse(preview.DraftId!), Ayse, "a", false, true));

        var row = Assert.Single(done.Rows);
        Assert.Equal("failed", row.Status);
        Assert.Equal("permission", row.Error);
    }

    /// <summary>
    /// Verifies that several records are validated per row and only the valid ones are written.
    /// </summary>
    [Fact]
    public async Task SeveralRecords_AreValidatedPerRow_AndOnlyTheValidOnesAreWritten()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "Alpha") }, new[] { ("telephone1", "1") }, new[] { ("name", "Gamma") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("şu hesapları ekle: Alpha, Gamma ve bir telefonlu", null, Ayse, true));

        Assert.Equal("preview", preview.Kind);
        Assert.Equal(new[] { "ready", "needs_info", "ready" }, preview.Rows.Select(r => r.Status));

        var done = Assert.IsType<CreateOutcome>(await rig.Service.ConfirmAsync(Guid.Parse(preview.DraftId!), Ayse, "a", false, true));

        Assert.Equal(new[] { "created", "skipped", "created" }, done.Rows.Select(r => r.Status));
        Assert.Equal(2, rig.Writer.Written.Count);
    }

    /// <summary>
    /// Verifies that too many records are refused.
    /// </summary>
    [Fact]
    public async Task TooManyRecords_AreRefused()
    {
        var rig = new Rig { MaxRows = 2 };
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "A") }, new[] { ("name", "B") }, new[] { ("name", "C") }));

        var outcome = await rig.Service.HandleAsync("şu hesapları ekle: A, B, C", null, Ayse, true);

        Assert.Equal("create_limit", Assert.IsType<FailureOutcome>(outcome).Kind);
    }

    /// <summary>
    /// Verifies that the daily limit stops writing.
    /// </summary>
    [Fact]
    public async Task TheDailyLimit_StopsWriting()
    {
        var rig = new Rig { PerDay = 1 };
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "A") }, new[] { ("name", "B") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("şu hesapları ekle: A, B", null, Ayse, true));

        var refused = await rig.Service.ConfirmAsync(Guid.Parse(preview.DraftId!), Ayse, "a", false, true);

        Assert.Equal("create_limit", Assert.IsType<FailureOutcome>(refused).Kind);
        Assert.Empty(rig.Writer.Written);
    }

    /// <summary>
    /// Verifies that an unknown table asks which one.
    /// </summary>
    [Fact]
    public async Task AnUnknownTable_AsksWhichOne()
    {
        var rig = new Rig();
        rig.Llm.Replies.Enqueue(Extraction("systemuser", new[] { ("name", "X") }));

        var outcome = await rig.Service.HandleAsync("hesap oluştur X", null, Ayse, true);

        Assert.Equal("create_table", Assert.IsType<FailureOutcome>(outcome).Kind);
    }

    /// <summary>
    /// Verifies that a refused attempt is audited once per reason without values.
    /// </summary>
    [Fact]
    public async Task ARefusedAttempt_IsAudited_OncePerReason_WithoutValues()
    {
        var rig = new Rig();

        await rig.Service.HandleAsync("Alpine adında hesap oluştur", null, Ayse, false, @"contoso\ayse");
        await rig.Service.HandleAsync("Alpine adında hesap oluştur", null, Ayse, false, @"contoso\ayse");

        var entry = Assert.Single(rig.Audit);
        Assert.Equal("record.create.denied", entry.Action);
        Assert.Equal(@"contoso\ayse", entry.Actor);
        Assert.Equal("no_permission", entry.Detail);
        Assert.DoesNotContain("Alpine", entry.Target + entry.Detail);
    }

    /// <summary>
    /// Verifies that the daily limit and too many rows are audited with the table.
    /// </summary>
    [Fact]
    public async Task TheDailyLimitAndTooManyRows_AreAuditedWithTheTable()
    {
        var rig = new Rig { PerDay = 1, MaxRows = 3 };
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "A") }, new[] { ("name", "B") }));
        var preview = Assert.IsType<CreateOutcome>(await rig.Service.HandleAsync("şu hesapları ekle: A, B", null, Ayse, true, @"contoso\ayse"));
        await rig.Service.ConfirmAsync(Guid.Parse(preview.DraftId!), Ayse, @"contoso\ayse", false, true);

        rig.MaxRows = 1;
        rig.Llm.Replies.Enqueue(Extraction("account", new[] { ("name", "A") }, new[] { ("name", "B") }));
        await rig.Service.HandleAsync("şu hesapları ekle: A, B", null, Ayse, true, @"contoso\ayse");

        Assert.Equal(new[] { "daily_limit", "too_many_rows" }, rig.Audit.Select(a => a.Detail));
        Assert.All(rig.Audit, a => Assert.Equal("account", a.Target));
    }

    /// <summary>
    /// Verifies that cancel removes the draft.
    /// </summary>
    [Fact]
    public void Cancel_RemovesTheDraft()
    {
        var rig = new Rig();
        var draft = new CreateDraft(Guid.NewGuid(), Ayse.UserId, "account", "q", new[] { (IReadOnlyDictionary<string, string>)new Dictionary<string, string>() }, DateTimeOffset.UtcNow);
        rig.Store.Save(draft);

        var outcome = rig.Service.Cancel(draft.Id, Ayse);

        Assert.Equal("cancelled", outcome.Kind);
        Assert.Null(rig.Store.Get(draft.Id, Ayse.UserId));
    }
}
