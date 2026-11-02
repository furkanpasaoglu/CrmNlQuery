using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.History.Services;

/// <summary>
/// Tests publishing and reopening saved results, owner-only access and truncation flags.
/// </summary>
public class ResultServiceTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";

    private sealed class RecordingExecutor : IQueryExecutor
    {
        private readonly string _json;
        public string? EntitySet { get; private set; }
        public string? FetchXml { get; private set; }
        public Guid? CallerId { get; private set; }
        public int Calls { get; private set; }
        public Exception? Throw { get; set; }
        public RecordingExecutor(string json) { _json = json; }
        /// <summary>
        /// Records the call and returns a canned table so tests can check the caller and query.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            EntitySet = entitySetName;
            FetchXml = fetchXml;
            CallerId = caller.UserIdOrNull();
            if (Throw is not null) throw Throw;
            return Task.FromResult(ResultTableBuilder.Build(JsonDocument.Parse(_json).RootElement.GetProperty("value").Clone(), fetchXml));
        }
    }

    private static (ResultService Service, IHistoryStore History, RecordingExecutor Executor) Create(string json = """{"value":[{"name":"Chair"}]}""")
    {
        var history = new MemoryHistoryStore();
        var executor = new RecordingExecutor(json);
        return (new ResultService(history, TestMetadata.Create(), executor), history, executor);
    }

    private static AnswerOutcome Answer(string entity = "product", string fetch = Fetch)
        => new AnswerOutcome(entity, "Showing Product", fetch, new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()), false);

    /// <summary>
    /// Verifies that opening a truncated result also carries the narrow hints but a small one does not.
    /// </summary>
    [Fact]
    public async Task OpeningATruncatedResult_AlsoCarriesTheNarrowHints_ButASmallOneDoesNot()
    {
        var big = "{\"value\":[" + string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"name\":\"P{i}\"}}")) + "]}";
        var (bigService, bigHistory, _) = Create(big);
        var (smallService, smallHistory, _) = Create();
        var owner = Guid.NewGuid();
        var bigId = await bigHistory.AddAsync(owner, "q", "product", Fetch);
        var smallId = await smallHistory.AddAsync(owner, "q", "product", Fetch);

        var truncated = await bigService.OpenAsync(bigId, owner);
        var small = await smallService.OpenAsync(smallId, owner);

        Assert.True(truncated!.Truncated);
        Assert.Contains(truncated.NarrowHints, h => h.Attribute == "statecode");
        Assert.False(small!.Truncated);
        Assert.Empty(small.NarrowHints);
    }

    /// <summary>
    /// Verifies that publish adds an ID that only the owner can open and records the question.
    /// </summary>
    [Fact]
    public async Task Publish_AddsAnIdThatOnlyTheOwnerCanOpen_AndRecordsTheQuestion()
    {
        var (service, history, _) = Create();
        var owner = Guid.NewGuid();
        var published = await service.PublishAsync(Answer(), owner, "Bütün productları getir");

        Assert.NotNull(published.ResultId);
        var entry = await history.GetAsync(owner, published.ResultId!);
        Assert.NotNull(entry);
        Assert.Equal("Bütün productları getir", entry!.Question);
        Assert.Null(await history.GetAsync(Guid.NewGuid(), published.ResultId!));
    }

    /// <summary>
    /// Verifies that publish keeps everything else the same.
    /// </summary>
    [Fact]
    public async Task Publish_KeepsEverythingElseTheSame()
    {
        var (service, _, _) = Create();
        var original = Answer();
        var published = await service.PublishAsync(original, Guid.NewGuid(), "q");
        Assert.Equal(original.FetchXml, published.FetchXml);
        Assert.Equal(original.Entity, published.Entity);
        Assert.Same(original.Table, published.Table);
    }

    /// <summary>
    /// Verifies that open re runs the stored query as the owner.
    /// </summary>
    [Fact]
    public async Task Open_ReRunsTheStoredQueryAsTheOwner()
    {
        var (service, _, executor) = Create();
        var owner = Guid.NewGuid();
        var id = (await service.PublishAsync(Answer(), owner, "q")).ResultId!;

        var result = await service.OpenAsync(id, owner);

        Assert.NotNull(result);
        Assert.Equal("Chair", result!.Table.Rows[0][0]);
        Assert.Equal("products", executor.EntitySet);
        Assert.Equal(Fetch, executor.FetchXml);
        Assert.Equal(owner, executor.CallerId);
    }

    /// <summary>
    /// Verifies that open by another user is not found and never touches CRM.
    /// </summary>
    [Fact]
    public async Task Open_ByAnotherUser_IsNotFound_AndNeverTouchesCrm()
    {
        var (service, _, executor) = Create();
        var id = (await service.PublishAsync(Answer(), Guid.NewGuid(), "q")).ResultId!;

        Assert.Null(await service.OpenAsync(id, Guid.NewGuid()));
        Assert.Equal(0, executor.Calls);
    }

    /// <summary>
    /// Verifies that open unknown ID is not found.
    /// </summary>
    [Fact]
    public async Task Open_UnknownId_IsNotFound()
    {
        var (service, _, executor) = Create();
        Assert.Null(await service.OpenAsync("nope", Guid.NewGuid()));
        Assert.Equal(0, executor.Calls);
    }

    /// <summary>
    /// Verifies that open flags truncation when the result fills the limit.
    /// </summary>
    [Fact]
    public async Task Open_FlagsTruncation_WhenTheResultFillsTheLimit()
    {
        var rows = string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"name\":\"P{i}\"}}"));
        var (service, _, _) = Create("{\"value\":[" + rows + "]}");
        var owner = Guid.NewGuid();
        var id = (await service.PublishAsync(Answer(), owner, "q")).ResultId!;

        var result = await service.OpenAsync(id, owner);
        Assert.True(result!.Truncated);
    }

    /// <summary>
    /// Verifies that open permission denied propagates for the endpoint to explain.
    /// </summary>
    [Fact]
    public async Task Open_PermissionDenied_PropagatesForTheEndpointToExplain()
    {
        var (service, _, executor) = Create();
        var owner = Guid.NewGuid();
        var id = (await service.PublishAsync(Answer(), owner, "q")).ResultId!;
        executor.Throw = new CrmException("You do not have permission to read Product records.", 403, null, true);

        var ex = await Assert.ThrowsAsync<CrmException>(() => service.OpenAsync(id, owner));
        Assert.True(ex.IsPermissionDenied);
    }

    /// <summary>
    /// Verifies that open entity no longer in metadata is not found.
    /// </summary>
    [Fact]
    public async Task Open_EntityNoLongerInMetadata_IsNotFound()
    {
        var (service, _, _) = Create();
        var owner = Guid.NewGuid();
        var id = (await service.PublishAsync(Answer(entity: "vanished"), owner, "q")).ResultId!;
        Assert.Null(await service.OpenAsync(id, owner));
    }
}
