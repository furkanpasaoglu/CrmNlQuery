using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Suggestions.Abstractions;
using CrmNlQuery.Application.Suggestions.Models;
using CrmNlQuery.Application.Suggestions.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

namespace CrmNlQuery.Tests.Application.Suggestions;

/// <summary>
/// Tests suggestion counters, which store only kind and count and count shown only for fresh answers.
/// </summary>
public class SuggestionStatsTests
{
    private static DateTimeOffset _now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that counters add up per kind and ignore unknown kinds.
    /// </summary>
    [Fact]
    public async Task Counters_AddUpPerKind_AndIgnoreUnknownKinds()
    {
        var stats = new MemorySuggestionStats(() => _now);

        await stats.AddShownAsync(new[] { "narrow", "group", "bogus" });
        await stats.AddShownAsync(new[] { "narrow" });
        await stats.AddClickAsync("narrow");
        await stats.AddClickAsync("bogus");

        var summary = await stats.SummaryAsync(7);
        Assert.Equal(new[] { "narrow", "group" }, summary.Select(s => s.Kind));
        Assert.Equal(2, summary[0].Shown);
        Assert.Equal(1, summary[0].Clicked);
        Assert.Equal(1, summary[1].Shown);
    }

    /// <summary>
    /// Verifies that the window counts only the last days and purge drops old days.
    /// </summary>
    [Fact]
    public async Task TheWindow_CountsOnlyTheLastDays_AndPurgeDropsOldDays()
    {
        var stats = new MemorySuggestionStats(() => _now);
        await stats.AddShownAsync(new[] { "sort" });
        var later = new MemorySuggestionStats(() => _now.AddDays(10));
        Assert.Empty(await new MemorySuggestionStats(() => _now.AddDays(10)).SummaryAsync(7));

        _now = _now.AddDays(10);
        await stats.AddShownAsync(new[] { "sort" });
        Assert.Equal(1, (await stats.SummaryAsync(7))[0].Shown);
        Assert.Equal(2, (await stats.SummaryAsync(30))[0].Shown);
        Assert.Equal(1, await stats.PurgeOlderThanAsync(_now.AddDays(-5)));
        Assert.Equal(1, (await stats.SummaryAsync(30))[0].Shown);
        _now = _now.AddDays(-10);
        Assert.NotNull(later);
    }

    private static AttributeInfo A(string name, string label, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static readonly EntityInfo Opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Opportunity", "Uniqueidentifier"), A("name", "Topic", "String"),
            A("statecode", "Status", "State", (0, "Open"), (1, "Won")), A("createdon", "Created On", "DateTime"),
        },
        new List<RelationshipInfo>());

    private sealed class Executor : IQueryExecutor
    {
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>> { new List<string> { "a" } }));
    }

    private const string Plain = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><attribute name=\"statecode\" /></entity></fetch>";

    private static AnswerOutcome Answer() => new AnswerOutcome("opportunity", "u", Plain,
        new ResultTable(new[] { "name", "statecode" }, new List<IReadOnlyList<string>> { new[] { "a", "Open" }, new[] { "b", "Won" }, new[] { "c", "Open" } }), false);

    private (SuggestionService Service, MemorySuggestionStats Stats, MemoryHistoryStore Store) Create()
    {
        var store = new MemoryHistoryStore();
        var stats = new MemorySuggestionStats(() => _now);
        var metadata = new FakeMetadataSource(Opportunity);
        var service = new SuggestionService(store, metadata, new PlanValidator(metadata), new ResultService(store, metadata, new Executor()), new ChatService(store), stats);
        return (service, stats, store);
    }

    /// <summary>
    /// Verifies that shown is counted only for a fresh answer.
    /// </summary>
    [Fact]
    public async Task Shown_IsCounted_OnlyForAFreshAnswer()
    {
        var (service, stats, _) = Create();

        await service.ForAsync(Answer(), countShown: false);
        Assert.Empty(await stats.SummaryAsync(7));

        var list = await service.ForAsync(Answer(), countShown: true);
        Assert.NotEmpty(list);
        Assert.Equal(list.Count, (await stats.SummaryAsync(7)).Sum(s => s.Shown));
    }

    /// <summary>
    /// Verifies that click is counted when a suggestion is applied and for a question style suggestion.
    /// </summary>
    [Fact]
    public async Task Click_IsCounted_WhenASuggestionIsApplied_AndForAQuestionStyleSuggestion()
    {
        var (service, stats, store) = Create();
        var me = Guid.NewGuid();
        var id = await store.AddAsync(me, "q", "opportunity", Plain, null, await new ChatService(store).EnsureAsync(me, null, "q"));

        await service.ApplyAsync(id, me, "narrow:statecode:1");
        await service.ClickAsync("related");
        await service.ClickAsync("not-a-kind");

        var summary = await stats.SummaryAsync(7);
        Assert.Equal(1, summary.Single(s => s.Kind == "narrow").Clicked);
        Assert.Equal(1, summary.Single(s => s.Kind == "related").Clicked);
        Assert.Equal(2, summary.Sum(s => s.Clicked));
    }

    private sealed class BrokenStats : ISuggestionStats
    {
        /// <summary>
        /// Always fails to simulate a broken stats store.
        /// </summary>
        public Task AddShownAsync(IReadOnlyList<string> kinds, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        /// <summary>
        /// Always fails to simulate a broken stats store.
        /// </summary>
        public Task AddClickAsync(string kind, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        /// <summary>
        /// Always fails to simulate a broken stats store.
        /// </summary>
        public Task<IReadOnlyList<SuggestionStat>> SummaryAsync(int days, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        /// <summary>
        /// Does nothing and reports zero records removed.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => throw new InvalidOperationException("db down");
    }

    /// <summary>
    /// Verifies that a failing counter never breaks the answer or the apply.
    /// </summary>
    [Fact]
    public async Task AFailingCounter_NeverBreaksTheAnswerOrTheApply()
    {
        var store = new MemoryHistoryStore();
        var metadata = new FakeMetadataSource(Opportunity);
        var service = new SuggestionService(store, metadata, new PlanValidator(metadata), new ResultService(store, metadata, new Executor()), new ChatService(store), new BrokenStats());
        var me = Guid.NewGuid();
        var id = await store.AddAsync(me, "q", "opportunity", Plain, null, await new ChatService(store).EnsureAsync(me, null, "q"));

        Assert.NotEmpty(await service.ForAsync(Answer(), countShown: true));
        Assert.IsType<AnswerOutcome>(await service.ApplyAsync(id, me, "narrow:statecode:1"));
    }
}
