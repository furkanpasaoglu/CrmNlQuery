using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Holds tests that the schema is refreshed once when a question matches no table.
/// </summary>
public class SchemaRefreshTests
{
    private sealed class CountingMetadata : IMetadataSource
    {
        private readonly FakeMetadataSource _inner = TestMetadata.Create();
        public int Refreshes { get; private set; }
        public bool Resolves { get; init; } = true;
        /// <summary>
        /// Test double metadata source that counts calls.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => _inner.ListEntitiesAsync(ct);
        /// <summary>
        /// Test double that returns entity info and counts the call.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => _inner.GetEntityAsync(logicalName, ct);
        /// <summary>
        /// Test double that counts a schema refresh.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default)
        {
            Refreshes++;
            return Task.CompletedTask;
        }
    }

    private sealed class Analyzer : IQuestionAnalyzer
    {
        private readonly CountingMetadata _metadata;
        public int Calls { get; private set; }
        public Analyzer(CountingMetadata metadata) { _metadata = metadata; }
        /// <summary>
        /// Test double analyzer that returns a canned analysis.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
        {
            Calls++;
            var entity = _metadata.Resolves && _metadata.Refreshes > 0 ? "product" : "gizmo";
            return Task.FromResult(new QuestionAnalysis(entity, new List<string>(), new List<Mention>(), new List<string>(), false));
        }
    }

    private sealed class NoSearch : IRecordSearcher
    {
        /// <summary>
        /// Test double record search that finds nothing.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(new List<RecordMatch>());
    }

    private sealed class NoViews : IDefaultViewSource
    {
        /// <summary>
        /// Test double view source that returns no default columns.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }

    private sealed class StopPlanner : IPlanGenerator
    {
        /// <summary>
        /// Test double planner that stops with a canned outcome.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default) => throw new InvalidOperationException("plan reached");
    }

    private sealed class NoExecutor : IQueryExecutor
    {
        /// <summary>
        /// Test double executor that must not be reached.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static AskPipeline Pipeline(CountingMetadata metadata, Analyzer analyzer, bool enabled, Func<DateTimeOffset> clock)
        => new AskPipeline(metadata, analyzer, new NoSearch(), new ColumnPolicy(new NoViews()), new StopPlanner(), new NoExecutor(),
            refreshSchemaWhenUnknown: enabled, clock: clock);

    private static AskRequest Ask() => new AskRequest("show gizmos", new Dictionary<string, string>(), Caller.ForEvaluation());

    private static async Task<AskOutcome> Run(AskPipeline pipeline)
    {
        try
        {
            return await pipeline.RunAsync(Ask());
        }
        catch (InvalidOperationException)
        {
            return new QuestionOutcome("planner", "", "plan reached", new List<Choice>(), false);
        }
    }

    /// <summary>
    /// Verifies that unknown table refreshes schema once and analyzes again.
    /// </summary>
    [Fact]
    public async Task UnknownTable_RefreshesSchemaOnce_AndAnalyzesAgain()
    {
        var metadata = new CountingMetadata();
        var analyzer = new Analyzer(metadata);
        var now = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        var pipeline = Pipeline(metadata, analyzer, true, () => now);

        await Run(pipeline);

        Assert.Equal(1, metadata.Refreshes);
        Assert.Equal(2, analyzer.Calls);
    }

    /// <summary>
    /// Verifies that refresh is rate limited to once per five minutes.
    /// </summary>
    [Fact]
    public async Task RefreshIsRateLimited_ToOncePerFiveMinutes()
    {
        var metadata = new CountingMetadata { Resolves = false };
        var analyzer = new Analyzer(metadata);
        var now = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        var pipeline = Pipeline(metadata, analyzer, true, () => now);

        await Run(pipeline);
        await Run(pipeline);
        Assert.Equal(1, metadata.Refreshes);

        now = now.AddMinutes(6);
        await Run(pipeline);
        Assert.Equal(2, metadata.Refreshes);
    }

    /// <summary>
    /// Verifies that disabled never refreshes.
    /// </summary>
    [Fact]
    public async Task Disabled_NeverRefreshes()
    {
        var metadata = new CountingMetadata();
        var analyzer = new Analyzer(metadata);
        var pipeline = Pipeline(metadata, analyzer, false, () => DateTimeOffset.UtcNow);

        var outcome = await Run(pipeline);

        Assert.Equal(0, metadata.Refreshes);
        Assert.Equal(1, analyzer.Calls);
        Assert.IsType<QuestionOutcome>(outcome);
    }
}
