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

namespace CrmNlQuery.Tests.Application.Create.Services;

/// <summary>
/// Unit tests for the creation service when records are supplied as JSON, covering preview and validation of rows.
/// </summary>
public class CreateServiceJsonTests
{
    private static readonly Caller Ayse = Caller.User(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));

    private sealed class NoLlm : ILlmClient
    {
        public int Calls { get; private set; }
        /// <summary>
        /// Returns scripted model output for the test instead of calling a real model.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("the JSON path must not call the model");
        }
    }

    private sealed class Schemas : ICreateSchemaSource
    {
        /// <summary>
        /// Returns a canned schema for the requested entity in the test.
        /// </summary>
        public Task<CreateSchema?> GetAsync(string entity, CancellationToken ct = default) => Task.FromResult(entity == "account" ? CreateValidatorTests.AccountSchema() : null);
    }

    private sealed class NoSearch : IRecordSearcher
    {
        /// <summary>
        /// Returns canned record matches for a name search in the test.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RecordMatch>>(Array.Empty<RecordMatch>());
    }

    private sealed class NoMeta : IMetadataSource
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

    private sealed class Writer : IRecordWriter
    {
        public int Written { get; private set; }
        /// <summary>
        /// Pretends to create a record and returns a new id without touching CRM.
        /// </summary>
        public Task<Guid> CreateAsync(CreateSchema schema, IReadOnlyDictionary<string, object?> payload, Caller caller, CancellationToken ct = default)
        {
            Written++;
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class Exec : IQueryExecutor
    {
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default) => Task.FromResult(new ResultTable(new[] { "name" }, Array.Empty<IReadOnlyList<string>>()));
    }

    private static (CreateService Service, NoLlm Llm, Writer Writer) Build(int maxRows = 50)
    {
        var llm = new NoLlm();
        var writer = new Writer();
        var service = new CreateService(new Schemas(), new CreateExtractor(llm), new NoSearch(), new NoMeta(), writer, new Exec(), new MemoryCreateDraftStore(), null,
            (e, c) => Task.CompletedTask, () => true, () => new[] { "account" }, () => maxRows, () => 200);
        return (service, llm, writer);
    }

    /// <summary>
    /// Verifies that pasted JSON goes to a preview without calling the model.
    /// </summary>
    [Fact]
    public async Task PastedJson_GoesToAPreview_WithoutCallingTheModel()
    {
        var (service, llm, writer) = Build();

        var outcome = Assert.IsType<CreateOutcome>(await service.HandleAsync("hesap oluştur [{\"name\":\"Alpha\"},{\"name\":\"Beta\",\"nonsense\":\"x\"},{\"telephone1\":\"1\"}]", null, Ayse, true));

        Assert.Equal(0, llm.Calls);
        Assert.Equal("preview", outcome.Kind);
        Assert.Equal(new[] { "ready", "needs_info", "needs_info" }, outcome.Rows.Select(r => r.Status));
        Assert.Contains(outcome.Rows[1].Problems, p => p.Code == "unknown_field");
        Assert.Equal(0, writer.Written);
    }

    /// <summary>
    /// Verifies that JSON without a verb or a table name is not a create request.
    /// </summary>
    [Fact]
    public async Task JsonWithoutAVerbOrATableName_IsNotACreateRequest()
    {
        var (service, _, _) = Build();

        Assert.False(await service.IsCandidateAsync("bu json ne demek {\"name\":\"Alpine\"}", null, Ayse));
        Assert.True(await service.IsCandidateAsync("{\"entity\":\"account\",\"name\":\"Alpine\"}", null, Ayse));
    }

    /// <summary>
    /// Verifies that nested JSON is refused with a clear message.
    /// </summary>
    [Fact]
    public async Task NestedJson_IsRefusedWithAClearMessage()
    {
        var (service, llm, _) = Build();

        var outcome = await service.HandleAsync("hesap oluştur {\"name\": {\"a\": 1}}", null, Ayse, true);

        Assert.Equal("create_failed", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Equal(0, llm.Calls);
    }

    /// <summary>
    /// Verifies that too many JSON records are refused.
    /// </summary>
    [Fact]
    public async Task TooManyJsonRecords_AreRefused()
    {
        var (service, _, _) = Build(maxRows: 2);

        var outcome = await service.HandleAsync("hesap ekle [{\"name\":\"A\"},{\"name\":\"B\"},{\"name\":\"C\"}]", null, Ayse, true);

        Assert.Equal("create_limit", Assert.IsType<FailureOutcome>(outcome).Kind);
    }
}
