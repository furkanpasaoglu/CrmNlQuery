using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Application.Quality.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Infrastructure.Persistence.Memory.Quality;

namespace CrmNlQuery.Tests.Application.Quality;

/// <summary>
/// Tests suggesting an answer to the acceptance set, which becomes a pending case for admin review.
/// </summary>
public class QualitySuggestionTests
{
    private const string Fetch = "<fetch><entity name='incident'><attribute name='title'/><order attribute='title'/></entity></fetch>";
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class Schema : IMetadataSource
    {
        private readonly EntityInfo _incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo>
            {
                new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
            }, new List<RelationshipInfo>());

        /// <summary>
        /// Returns the test schema's entity list.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary> { new EntitySummary("incident", "incidents", "Case") });

        /// <summary>
        /// Returns the test schema's entity definition, or null when unknown.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => Task.FromResult<EntityInfo?>(logicalName == "incident" ? _incident : null);

        /// <summary>
        /// Does nothing because the fake metadata never changes.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Executor : IQueryExecutor
    {
        public int Rows = 3;

        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(new ResultTable(new[] { "Case Title" }, Enumerable.Range(0, Rows).Select(i => (IReadOnlyList<string>)new[] { "row " + i }).ToList()));
    }

    private sealed class Harness
    {
        public readonly MemoryQualityStore Store = new MemoryQualityStore();
        public readonly MemoryHistoryStore History = new MemoryHistoryStore();
        public readonly Executor Executor = new Executor();
        public readonly List<string> Audit = new List<string>();
        public bool Enabled = true;
        public bool Suggestions = true;
        public int Max = 500;
        public DateTimeOffset Now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        public QualityService Service;

        public Harness()
        {
            Service = new QualityService(Store, new Schema(), Executor, () => Enabled, () => Max,
                (actor, action, target, detail, ct) => { Audit.Add(actor + "|" + action + "|" + target + "|" + detail); return Task.CompletedTask; },
                clock: () => Now, history: History, suggestionsAllowed: () => Suggestions);
        }

        /// <summary>
        /// Creates a stored answer for the test.
        /// </summary>
        public Task<string> Answer(Guid owner, string question = "Show my cases", string fetch = Fetch, int? steps = null, string? kind = null, string entity = "incident")
            => History.AddAsync(owner, question, entity, fetch, steps: steps, kind: kind);

        /// <summary>
        /// Calls the suggestion flow as the given user.
        /// </summary>
        public Task<QualitySaveResult> Suggest(Guid owner, string id) => Service.SuggestFromResultAsync(owner, id, Caller.ForEvaluation(), "en");
    }

    /// <summary>
    /// Verifies that a suggestion becomes a pending unverified case without naming the user.
    /// </summary>
    [Fact]
    public async Task ASuggestionBecomesAPendingUnverifiedCaseWithoutNamingTheUser()
    {
        var h = new Harness();
        var id = await h.Answer(Alice, "Show my open cases");

        var result = await h.Suggest(Alice, id);

        Assert.Equal(QualitySaveStatus.Saved, result.Status);
        var item = Assert.Single(await h.Store.ListAsync(new QualityCaseFilter()));
        Assert.Equal(QualityCase.SourceSuggested, item.Source);
        Assert.Equal(QualityCase.StatusPending, item.Status);
        Assert.False(item.Verified);
        Assert.Equal("Show my open cases", item.Question);
        Assert.Equal(Fetch, item.ReferenceFetch);
        Assert.DoesNotContain(Alice.ToString(), item.CreatedBy);
        Assert.Contains(h.Audit, a => a.StartsWith("(user suggestion)|quality.suggest|"));
        Assert.DoesNotContain(h.Audit, a => a.Contains("Show my open cases") || a.Contains(Alice.ToString()));
    }

    /// <summary>
    /// Verifies that a pending suggestion does not run until an administrator approves it.
    /// </summary>
    [Fact]
    public async Task APendingSuggestionDoesNotRunUntilAnAdministratorApprovesIt()
    {
        var h = new Harness();
        var id = await h.Answer(Alice);
        await h.Suggest(Alice, id);
        var pendingId = (await h.Store.ListAsync(new QualityCaseFilter())).Single().Id;

        Assert.Empty(await h.Store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusActive)));
        Assert.Single(await h.Store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusPending)));

        var approved = await h.Service.ApproveAsync(new[] { pendingId }, @"LAB\boss");

        Assert.Equal(1, approved);
        var item = Assert.Single(await h.Store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusActive)));
        Assert.True(item.Verified);
        Assert.Empty(await h.Store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusPending)));
    }

    /// <summary>
    /// Verifies that approving an ordinary retired case does not reactivate it.
    /// </summary>
    [Fact]
    public async Task ApprovingAnOrdinaryRetiredCaseDoesNotReactivateIt()
    {
        var h = new Harness();
        await h.Store.AddAsync(new QualityCase { CreatedBy = "x", CreatedAt = h.Now, Source = QualityCase.SourceGenerated, Status = QualityCase.StatusRetired, Question = "q", Entity = "incident", ReferenceFetch = Fetch });
        var id = (await h.Store.ListAsync(new QualityCaseFilter())).Single().Id;

        await h.Service.ApproveAsync(new[] { id }, @"LAB\boss");

        var item = await h.Store.GetAsync(id);
        Assert.True(item!.Verified);
        Assert.Equal(QualityCase.StatusRetired, item.Status);
    }

    /// <summary>
    /// Verifies that each user may suggest at most ten examples a day and the limit resets tomorrow.
    /// </summary>
    [Fact]
    public async Task EachUserMaySuggestAtMostTenExamplesADayAndTheLimitResetsTomorrow()
    {
        var h = new Harness();
        for (var i = 0; i < QualityService.MaxSuggestionsPerUserPerDay; i++)
        {
            var id = await h.Answer(Alice, "question " + i);
            Assert.Equal(QualitySaveStatus.Saved, (await h.Suggest(Alice, id)).Status);
        }

        var extra = await h.Answer(Alice, "one too many");
        Assert.Equal(QualitySaveStatus.TooManySuggestions, (await h.Suggest(Alice, extra)).Status);
        var bobId = await h.Answer(Bob, "bob question");
        Assert.Equal(QualitySaveStatus.Saved, (await h.Suggest(Bob, bobId)).Status);

        h.Now = h.Now.AddDays(1);
        Assert.Equal(QualitySaveStatus.Saved, (await h.Suggest(Alice, extra)).Status);
    }

    /// <summary>
    /// Verifies that the same question and query is not added twice.
    /// </summary>
    [Fact]
    public async Task TheSameQuestionAndQueryIsNotAddedTwice()
    {
        var h = new Harness();
        var first = await h.Answer(Alice, "same question");
        var second = await h.Answer(Bob, "same question");

        Assert.Equal(QualitySaveStatus.Saved, (await h.Suggest(Alice, first)).Status);
        Assert.Equal(QualitySaveStatus.Duplicate, (await h.Suggest(Bob, second)).Status);
        Assert.Single(await h.Store.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that only the owners plain answers can be suggested.
    /// </summary>
    [Fact]
    public async Task OnlyTheOwnersPlainAnswersCanBeSuggested()
    {
        var h = new Harness();
        var others = await h.Answer(Bob, "bob's answer");
        var chain = await h.Answer(Alice, "two steps", steps: 2);
        var summary = await h.Answer(Alice, "summary of an account", kind: "summary");
        var edited = await h.Answer(Alice, "Show my cases (edited: removed a filter)");
        var closed = await h.Answer(Alice, "closed table", entity: "systemuser", fetch: "<fetch><entity name='systemuser'><attribute name='fullname'/></entity></fetch>");

        Assert.Equal(QualitySaveStatus.NotFound, (await h.Suggest(Alice, others)).Status);
        Assert.Equal(QualitySaveStatus.NotSupported, (await h.Suggest(Alice, chain)).Status);
        Assert.Equal(QualitySaveStatus.NotFound, (await h.Suggest(Alice, summary)).Status);
        Assert.Equal(QualitySaveStatus.NotSupported, (await h.Suggest(Alice, edited)).Status);
        Assert.Equal(QualitySaveStatus.DataClosed, (await h.Suggest(Alice, closed)).Status);
        Assert.Equal(QualitySaveStatus.NotFound, (await h.Suggest(Alice, "no-such-result")).Status);
        Assert.Empty(await h.Store.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that an empty result is too weak to suggest.
    /// </summary>
    [Fact]
    public async Task AnEmptyResultIsTooWeakToSuggest()
    {
        var h = new Harness();
        h.Executor.Rows = 0;
        var id = await h.Answer(Alice);

        Assert.Equal(QualitySaveStatus.TooWeak, (await h.Suggest(Alice, id)).Status);
    }

    /// <summary>
    /// Verifies that suggesting is refused when the feature or the setting is off.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SuggestingIsRefusedWhenTheFeatureOrTheSettingIsOff(bool enabled, bool suggestions)
    {
        var h = new Harness { Enabled = enabled, Suggestions = suggestions };
        var id = await h.Answer(Alice);

        Assert.Equal(QualitySaveStatus.Disabled, (await h.Suggest(Alice, id)).Status);
        Assert.False(h.Service.SuggestionsAllowed);
        Assert.Empty(await h.Store.ListAsync(new QualityCaseFilter()));
    }

    /// <summary>
    /// Verifies that too many pending suggestions stop further ones.
    /// </summary>
    [Fact]
    public async Task TooManyPendingSuggestionsStopFurtherOnes()
    {
        var h = new Harness();
        for (var i = 0; i < QualityService.MaxPending; i++)
        {
            await h.Store.AddAsync(new QualityCase { CreatedBy = "(user suggestion)", CreatedAt = h.Now, Source = QualityCase.SourceSuggested, Status = QualityCase.StatusPending, Question = "p" + i, Entity = "incident", ReferenceFetch = Fetch });
        }
        var id = await h.Answer(Alice);

        Assert.Equal(QualitySaveStatus.LimitReached, (await h.Suggest(Alice, id)).Status);
    }

    /// <summary>
    /// Verifies that the acceptance set limit also applies to suggestions.
    /// </summary>
    [Fact]
    public async Task TheAcceptanceSetLimitAlsoAppliesToSuggestions()
    {
        var h = new Harness { Max = 1 };
        await h.Store.AddAsync(new QualityCase { CreatedBy = "x", CreatedAt = h.Now, Question = "existing", Entity = "incident", ReferenceFetch = Fetch });
        var id = await h.Answer(Alice, "another one");

        Assert.Equal(QualitySaveStatus.LimitReached, (await h.Suggest(Alice, id)).Status);
    }
}
