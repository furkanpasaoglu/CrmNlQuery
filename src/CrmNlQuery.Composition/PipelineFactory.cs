using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Counting.Abstractions;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Feedback.Abstractions;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Preferences.Abstractions;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;
using CrmNlQuery.Infrastructure.Persistence.Memory.Quality;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Quality;

namespace CrmNlQuery.Composition;

/// <summary>
/// Composes the ask pipeline and the various stores and services from options, choosing PostgreSQL or in-memory storage.
/// </summary>
public static class PipelineFactory
{
    /// <summary>
    /// Builds the ask pipeline from CRM and Ollama options with the given metadata source, knowledge, limits and logger.
    /// </summary>
    public static AskPipeline Create(CrmOptions crm, OllamaOptions ollama, out IMetadataSource metadata, IKnowledgeSource? knowledge = null, ILimitsSource? limits = null,
        ILogger<AskPipeline>? logger = null, bool evaluation = false)
    {
        return Create(crm, OllamaClient.Create(ollama), out metadata, logger: logger, knowledge: knowledge, limits: limits, evaluation: evaluation);
    }

    /// <summary>
    /// Creates the history and result store, PostgreSQL with schema migrations when a connection string is given, otherwise in-memory.
    /// </summary>
    public static async Task<IHistoryStore> CreateHistoryStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryHistoryStore();
        }
        return await PostgresHistoryStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the feedback store for votes, metadata and shared candidates, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<IFeedbackStore> CreateFeedbackStoreAsync(string? connectionString, IHistoryStore history)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryFeedbackStore(history is MemoryHistoryStore memory ? memory.Exists : _ => true);
        }
        return await PostgresFeedbackStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the quality acceptance-set store, PostgreSQL if a connection string is given, otherwise in-memory.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Quality.Abstractions.IQualityStore> CreateQualityStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryQualityStore();
        }
        return await PostgresQualityStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the store for quality check runs and case results, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Quality.Abstractions.IQualityRunStore> CreateQualityRunStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryQualityRunStore();
        }
        return await PostgresQualityRunStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the design assistant job store, PostgreSQL if configured, otherwise in-memory, with request text visible only to the owner.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Design.Abstractions.IDesignJobStore> CreateDesignJobStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryDesignJobStore();
        }
        return await PostgresDesignJobStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the suggestion counters store (shown and clicked per kind, no text), PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Suggestions.Abstractions.ISuggestionStats> CreateSuggestionStatsAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemorySuggestionStats();
        }
        return await PostgresSuggestionStats.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the customer summary builder using the filtered schema and an executor that runs as the caller and writes the access log.
    /// </summary>
    public static SummaryBuilder CreateSummaryBuilder(CrmOptions crm, IMetadataSource metadata, IAccessLogStore? accessLog, Func<TimeSpan> sectionTimeout, ILogger? logger = null)
    {
        return new SummaryBuilder(metadata, CreateExecutor(crm, accessLog), new DefaultViewReader(CrmClient.Create(crm)), sectionTimeout, null, logger);
    }

    /// <summary>
    /// Creates the email draft service using the same filtered schema and executor as summary reads, with the sender name read from the caller's own user record.
    /// </summary>
    public static CrmNlQuery.Application.Drafts.Services.DraftService CreateDraftService(IMetadataSource metadata, IQueryExecutor executor, ILlmClient llm)
    {
        var reads = new SummaryBuilder(metadata, executor, new CrmNlQuery.Application.Drafts.Services.NoDefaultViews());
        return new CrmNlQuery.Application.Drafts.Services.DraftService(new CrmNlQuery.Application.Drafts.Services.DraftValueReader(metadata, executor, reads), new CrmNlQuery.Application.Drafts.Services.DraftComposer(llm),
            async (caller, ct) =>
            {
                if (caller.IsEvaluation || caller.UserId == Guid.Empty)
                {
                    return null;
                }
                try
                {
                    var fetch = $"""<fetch top="1"><entity name="systemuser"><attribute name="fullname" /><filter><condition attribute="systemuserid" operator="eq" value="{caller.UserId}" /></filter></entity></fetch>""";
                    var table = await executor.ExecuteAsync("systemusers", fetch, caller, ct);
                    return table.Rows.Count > 0 && table.Rows[0].Count > 0 ? table.Rows[0][0] : null;
                }
                catch (CrmNlQuery.Application.Common.Exceptions.CrmException)
                {
                    return null;
                }
            });
    }

    /// <summary>
    /// Creates the data access log store (who, which tables, how many rows, no text), append-only PostgreSQL with triggers or in-memory.
    /// </summary>
    public static async Task<IAccessLogStore> CreateAccessLogStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryAccessLogStore();
        }
        return await PostgresAccessLogStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the usage metrics store, PostgreSQL with schema migrations if configured, otherwise in-memory.
    /// </summary>
    public static async Task<IMetricsStore> CreateMetricsStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryMetricsStore();
        }
        return await PostgresMetricsStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the admin panel access store for users, roles, permissions and audit, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<IAccessStore> CreateAccessStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryAccessStore();
        }
        return await PostgresAccessStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the knowledge base document store, PostgreSQL with pgvector if configured, otherwise in-memory.
    /// </summary>
    public static async Task<IKnowledgeStore> CreateKnowledgeStoreAsync(string? connectionString, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryKnowledgeStore();
        }
        return await PostgresKnowledgeStore.CreateOrUnavailableAsync(connectionString,
            reason => logger?.LogWarning("The knowledge base schema could not be created and stays OFF; everything else runs. Install the pgvector extension in PostgreSQL (and restart the service). Database said: {Reason}", reason));
    }

    /// <summary>
    /// Creates the store for panel setting overrides, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<ISettingsStore> CreateSettingsStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemorySettingsStore();
        }
        return await PostgresSettingsStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the counter that gets the total record count of a truncated result using caller-scoped count queries with an access log entry per count.
    /// </summary>
    public static IRecordCounter CreateRecordCounter(CrmOptions crm, IAccessLogStore? accessLog = null)
    {
        IRecordCounter counter = new CrmRecordCounter(CrmClient.Create(crm));
        return accessLog is null ? counter : new AccessLoggingRecordCounter(counter, accessLog);
    }

    /// <summary>
    /// Creates the CRM store that writes the user's personal views on the user's behalf for Open in CRM.
    /// </summary>
    public static IUserViewStore CreateUserViews(CrmOptions crm)
    {
        return new CrmUserViews(CrmClient.Create(crm));
    }

    private static Func<CancellationToken, Task<string>>? PolicyVersion(IDataPolicy? dataPolicy)
        => dataPolicy is null ? null : async ct => (await dataPolicy.GetAsync(ct)).Version;

    /// <summary>
    /// Creates the data policy allowlist rule store, persistent if PostgreSQL is configured, otherwise in-memory.
    /// </summary>
    public static async Task<IDataPolicyStore> CreateDataPolicyStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryDataPolicyStore();
        }
        return await PostgresDataPolicyStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the business glossary store, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Glossary.Abstractions.IGlossaryStore> CreateGlossaryStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryGlossaryStore();
        }
        return await PostgresGlossaryStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the report usage counter store, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Access.Abstractions.IAuthChallengeStore> CreateAuthChallengeStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new CrmNlQuery.Infrastructure.Persistence.Memory.Access.MemoryAuthChallengeStore();
        }
        return await CrmNlQuery.Infrastructure.Persistence.Postgres.Access.PostgresAuthChallengeStore.CreateAsync(connectionString);
    }

    public static async Task<CrmNlQuery.Application.Reports.Abstractions.IReportStats> CreateReportStatsAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryReportStats();
        }
        return await PostgresReportStats.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the report catalog store, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<CrmNlQuery.Application.Reports.Abstractions.IReportCatalogStore> CreateReportCatalogStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryReportCatalogStore();
        }
        return await PostgresReportCatalogStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the user preferences store, PostgreSQL if configured, otherwise in-memory.
    /// </summary>
    public static async Task<IPreferencesStore> CreatePreferencesStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryPreferencesStore();
        }
        return await PostgresPreferencesStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the application log store for the admin panel, PostgreSQL if configured, otherwise in-memory with recent entries.
    /// </summary>
    public static async Task<ILogStore> CreateLogStoreAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MemoryLogStore();
        }
        return await PostgresLogStore.CreateAsync(connectionString);
    }

    /// <summary>
    /// Creates the query executor used by the result page to re-run queries, writing each user query to the data access log when a log store is given.
    /// </summary>
    public static IQueryExecutor CreateExecutor(CrmOptions crm, IAccessLogStore? accessLog = null)
    {
        IQueryExecutor executor = new FetchQueryExecutor(CrmClient.Create(crm));
        return accessLog is null ? executor : new AccessLoggingQueryExecutor(executor, accessLog);
    }

    /// <summary>
    /// Creates the record creation service that reads writable field schema from the CRM and writes the record as the caller.
    /// </summary>
    public static CrmNlQuery.Application.Create.Services.CreateService CreateRecordCreation(CrmOptions crm, IMetadataSource metadata, IAccessLogStore? accessLog, ILlmClient llm, IDataPolicy? policy,
        Func<CrmNlQuery.Domain.Access.Entities.AuditEntry, CancellationToken, Task> audit, Func<bool> enabled, Func<IReadOnlyList<string>> tables, Func<int> maxRows, Func<int> perDay,
        ILogger? logger = null, Func<IReadOnlyCollection<string>>? hiddenFields = null)
    {
        var client = CrmClient.Create(crm);
        IRecordSearcher searcher = new CrmRecordSearcher(new FetchQueryExecutor(client));
        if (accessLog is not null)
        {
            searcher = new AccessLoggingRecordSearcher(searcher, accessLog);
        }
        return new CrmNlQuery.Application.Create.Services.CreateService(new CrmCreateSchemaProvider(client, metadata), new CrmNlQuery.Application.Create.Services.CreateExtractor(llm, logger, hiddenFields), searcher, metadata,
            new CrmRecordWriter(client), CreateExecutor(crm, accessLog), new CrmNlQuery.Infrastructure.Persistence.Memory.Create.MemoryCreateDraftStore(), policy, audit, enabled, tables, maxRows, perDay);
    }

    /// <summary>
    /// Creates the caller resolver that maps a Windows account to a CRM user with a ten-minute cache.
    /// </summary>
    public static ICallerResolver CreateCallerResolver(CrmOptions crm)
    {
        return new CachingCallerResolver(new CrmCallerResolver(CrmClient.Create(crm)), () => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Builds the ask pipeline around a ready LLM client, with an optional plan cache lifetime for reusing validated plans.
    /// </summary>
    public static AskPipeline Create(CrmOptions crm, ILlmClient llm, out IMetadataSource metadata,
        Func<string, ILlmClient>? forPurpose = null, ILogger<AskPipeline>? logger = null,
        TimeSpan? planCacheTtl = null, IKnowledgeSource? knowledge = null, ILimitsSource? limits = null, Func<TimeSpan>? planCacheTtlProvider = null, bool evaluation = false, IAccessLogStore? accessLog = null,
        TimeSpan? analysisCacheTtl = null, bool pruneContext = true, IDataPolicy? dataPolicy = null,
        Func<IMetadataSource, SummaryBuilder?>? summaryFactory = null, Func<bool>? summaryEnabled = null, Func<TimeSpan>? metadataTtl = null, bool refreshSchemaWhenUnknown = false, IReadOnlyList<string>? exampleQuestions = null, IReadOnlyList<string>? exampleQuestionsTurkish = null,
        Func<IMetadataSource, IQueryExecutor, ILlmClient, CrmNlQuery.Application.Drafts.Services.DraftService?>? draftFactory = null, Func<bool>? draftEnabled = null,
        Func<IMetadataSource, CrmNlQuery.Application.Glossary.Services.GlossaryService?>? glossaryFactory = null)
    {
        Func<string, ILlmClient> clientFor = forPurpose ?? (_ => llm);
        var client = CrmClient.Create(crm, allowEvaluationCaller: evaluation);
        var crmProvider = new MetadataProvider(client, crm.MetadataCacheDir, () => DateTimeOffset.UtcNow, metadataTtl);
        IMetadataSource provider = dataPolicy is null ? crmProvider : new PolicyMetadataSource(crmProvider, dataPolicy);
        IQueryExecutor executor = new FetchQueryExecutor(client);
        IRecordSearcher searcher = new CrmRecordSearcher(new FetchQueryExecutor(client));
        if (accessLog is not null)
        {
            executor = new AccessLoggingQueryExecutor(executor, accessLog);
            searcher = new AccessLoggingRecordSearcher(searcher, accessLog);
        }
        var validator = new PlanValidator(provider, limits);

        IPlanGenerator planner = new LlmPlanGenerator(clientFor("plan"), validator, limits, new DistinctRewriter(provider), pruneContext);
        if (planCacheTtlProvider is not null)
        {
            planner = new CachingPlanGenerator(planner, planCacheTtlProvider, policyVersion: PolicyVersion(dataPolicy));
        }
        else if (planCacheTtl is { } ttl && ttl > TimeSpan.Zero)
        {
            planner = new CachingPlanGenerator(planner, ttl, policyVersion: PolicyVersion(dataPolicy));
        }

        metadata = provider;
        var summaryBuilder = summaryFactory?.Invoke(provider);
        return new AskPipeline(
            provider,
            new LlmQuestionAnalyzer(clientFor("analyze")),
            searcher,
            new ColumnPolicy(new DefaultViewReader(client)),
            planner,
            executor,
            new LlmQuestionDecomposer(clientFor("decompose")),
            logger,
            knowledge,
            limits,
            new StepInputFiller(validator),
            new StepIdColumn(provider),
            analysisCacheTtl is { } analysisTtl && analysisTtl > TimeSpan.Zero ? new AnalysisCache(() => analysisTtl) : null,
            summaryBuilder, summaryEnabled, refreshSchemaWhenUnknown, exampleQuestions: exampleQuestions, exampleQuestionsTurkish: exampleQuestionsTurkish,
            draft: draftFactory?.Invoke(provider, executor, clientFor("draft")), draftEnabled: draftEnabled,
            glossary: glossaryFactory?.Invoke(provider));
    }
}
