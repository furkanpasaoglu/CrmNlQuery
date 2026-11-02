using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Operations.Settings;
using CrmNlQuery.Application.Operations.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Application.CrmViews.Services;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.Feedback.Services;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Application.Observability.Models;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Application.Preferences.Services;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Infrastructure.Persistence.Postgres;

namespace CrmNlQuery.Composition;

/// <summary>
/// Composition root of the service: reads settings, selects and wires the infrastructure (CRM, model, stores) and builds the use cases.
/// </summary>
public static class AppComposition
{
    /// <summary>
    /// Returns the CRM base address so CORS origins can be set at startup before services are built.
    /// </summary>
    public static string CrmBaseUrl(IConfiguration configuration)
        => (configuration.GetSection("Crm").Get<CrmOptions>() ?? new CrmOptions()).BaseUrl;

    /// <summary>
    /// Builds all services from configuration and logging, wiring infrastructure into use cases.
    /// </summary>
    public static async Task<AppServices> CreateAsync(IConfiguration configuration, ILoggerFactory loggerFactory, string environmentName, CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("CrmNlQuery.Composition");

        var crmOptions = configuration.GetSection("Crm").Get<CrmOptions>() ?? new CrmOptions();
        if (Uri.TryCreate(crmOptions.BaseUrl, UriKind.Absolute, out var crmUri) && crmUri.Scheme == Uri.UriSchemeHttp && !crmUri.IsLoopback)
        {
            logger.LogWarning("The CRM is reached over plain HTTP ({Host}): the service account credentials and CRM data travel unencrypted. Use an https Crm:BaseUrl outside the lab.", crmUri.Host);
        }
        var ollamaOptions = configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
        var llmSelection = configuration.GetSection("Llm").Get<LlmSelection>() ?? new LlmSelection();
        var anthropicOptions = configuration.GetSection("Anthropic").Get<AnthropicOptions>() ?? new AnthropicOptions();
        if (LlmClientFactory.IsCloud(llmSelection))
        {
            logger.LogWarning("Questions and metadata are sent to the Claude API ({Model}). Lab use with fake data only; remove before customer delivery.", anthropicOptions.Model);
        }
        var vllmOptions = configuration.GetSection("Vllm").Get<VllmOptions>() ?? new VllmOptions();
        var rawLlm = LlmClientFactory.Create(llmSelection, ollamaOptions, anthropicOptions, vllmOptions);
        var (llmProvider, llmModel) = LlmClientFactory.Describe(llmSelection, ollamaOptions, anthropicOptions, vllmOptions);
        Func<string, ILlmClient> localStep = purpose => LlmClientFactory.IsCloud(llmSelection) ? rawLlm : LlmClientFactory.CreateForStep(llmSelection, ollamaOptions, vllmOptions, purpose);

        var connection = configuration["History:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connection))
        {
            await WaitForDatabaseAsync(connection, configuration, logger, ct);
        }
        var metricsStore = await PipelineFactory.CreateMetricsStoreAsync(connection);
        var logStore = await PipelineFactory.CreateLogStoreAsync(connection);
        var historyStore = await PipelineFactory.CreateHistoryStoreAsync(connection);
        var feedbackStore = await PipelineFactory.CreateFeedbackStoreAsync(connection, historyStore);
        var qualityStore = await PipelineFactory.CreateQualityStoreAsync(connection);
        var qualityRunStore = await PipelineFactory.CreateQualityRunStoreAsync(connection);
        var suggestionStats = await PipelineFactory.CreateSuggestionStatsAsync(connection);
        var designJobStore = await PipelineFactory.CreateDesignJobStoreAsync(connection);
        var accessStore = await PipelineFactory.CreateAccessStoreAsync(connection);
        var knowledgeStore = await PipelineFactory.CreateKnowledgeStoreAsync(connection, logger);
        var accessLogStore = await PipelineFactory.CreateAccessLogStoreAsync(connection);

        var settingsStore = await PipelineFactory.CreateSettingsStoreAsync(connection);
        var preferencesStore = await PipelineFactory.CreatePreferencesStoreAsync(connection);
        var preferences = new PreferencesService(preferencesStore);
        var settings = new RuntimeSettings(settingsStore, key => configuration[key], (entry, token) => accessStore.AddAuditAsync(entry, token),
            onStoreError: ex => logger.LogError(ex, "Runtime settings could not be read or written; using the last known values ({ExceptionType})", ex.GetType().Name));
        await settings.InitializeAsync();
        var limits = new RuntimeLimits(settings);

        var dataPolicyStore = await PipelineFactory.CreateDataPolicyStoreAsync(connection);
        var dataPolicy = new DataPolicyService(dataPolicyStore, () => DataPolicyDefaults.Parse(configuration["DataPolicy:DefaultTables"]),
            () => settings.Get(SettingsCatalog.DataPolicy) != "off", logger: loggerFactory.CreateLogger("CrmNlQuery.DataPolicy"));
        var starting = DataPolicyDefaults.Parse(configuration["DataPolicy:DefaultTables"]);
        logger.LogInformation("Data access list: {Count} tables are open by default ({Tables}); every other table stays CLOSED until an administrator opens it in the admin panel (Data access). Fields with sensitive-looking names are closed automatically.",
            starting.Count, string.Join(", ", starting.OrderBy(x => x, StringComparer.Ordinal)));
        if (!dataPolicyStore.Persistent)
        {
            logger.LogWarning("Data access decisions are kept in MEMORY only (History:ConnectionString is not set): tables or fields an administrator closes or opens are forgotten when the app pool restarts.");
        }
        if (settings.Get(SettingsCatalog.DataPolicy) == "off")
        {
            logger.LogWarning("The data access list is switched OFF (Access:DataPolicy=off): closed tables and fields are NOT filtered. Use this for emergencies only.");
        }

        ILlmClient llm = rawLlm;
        if (LlmClientFactory.IsCloud(llmSelection))
        {
            var budget = new CloudBudget();
            var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            try
            {
                var today = await metricsStore.GetSummaryAsync(dayStart, dayStart.AddDays(1));
                budget.Seed(today.InputTokens + today.OutputTokens);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read today's token usage; the cloud budget counter starts at zero.");
            }
            llm = new BudgetedLlmClient(rawLlm, limits, budget);
        }

        var knowledgeFile = configuration.GetSection("Knowledge").Get<KnowledgeOptions>() ?? new KnowledgeOptions();
        KnowledgeOptions LiveKnowledgeOptions() => new KnowledgeOptions
        {
            Enabled = settings.GetBool(SettingsCatalog.KnowledgeEnabled),
            MinSimilarity = (double)settings.GetDecimal(SettingsCatalog.KnowledgeMinSimilarity),
            TopK = settings.GetInt(SettingsCatalog.KnowledgeTopK),
            MaxChars = settings.GetInt(SettingsCatalog.KnowledgeMaxChars),
            EmbeddingModel = knowledgeFile.EmbeddingModel,
            ChunkChars = knowledgeFile.ChunkChars,
            Candidates = knowledgeFile.Candidates,
        };
        var knowledge = new KnowledgeService(knowledgeStore, OllamaEmbeddingClient.Create(ollamaOptions, knowledgeFile.EmbeddingModel), LiveKnowledgeOptions,
            (entry, token) => accessStore.AddAuditAsync(entry, token));
        var knowledgeSource = new KnowledgeRetriever(knowledge, () => settings.GetBool(SettingsCatalog.KnowledgeEnabled), loggerFactory.CreateLogger<KnowledgeRetriever>());
        if (settings.GetBool(SettingsCatalog.KnowledgeEnabled))
        {
            logger.LogInformation("Company knowledge is ON: questions are matched against the knowledge documents with the local {Model} embedding model.", knowledgeFile.EmbeddingModel);
        }

        var glossaryStore = await PipelineFactory.CreateGlossaryStoreAsync(connection);
        var reportCatalogStore = await PipelineFactory.CreateReportCatalogStoreAsync(connection);
        var reportStats = await PipelineFactory.CreateReportStatsAsync(connection);
        CrmNlQuery.Application.Glossary.Services.GlossaryService? glossaryService = null;
        var pipeline = PipelineFactory.Create(crmOptions, llm, out var metadata,
            purpose => new MeteredLlmClient(localStep(purpose),
                metricsStore, purpose, llmProvider, llmModel, logger: loggerFactory.CreateLogger("CrmNlQuery.ModelCalls")),
            loggerFactory.CreateLogger<AskPipeline>(),
            planCacheTtlProvider: () => TimeSpan.FromMinutes(settings.GetInt(SettingsCatalog.PlanCacheMinutes)),
            knowledge: knowledgeSource,
            limits: limits,
            accessLog: accessLogStore,
            analysisCacheTtl: TimeSpan.FromMinutes(configuration.GetValue("Planning:AnalysisCacheMinutes", 10)),
            pruneContext: configuration.GetValue("Planning:PruneContext", true),
            dataPolicy: dataPolicy,
            summaryFactory: schema => PipelineFactory.CreateSummaryBuilder(crmOptions, schema, accessLogStore, () => TimeSpan.FromSeconds(settings.GetInt(SettingsCatalog.SummarySectionTimeoutSeconds)), loggerFactory.CreateLogger("CrmNlQuery.Summary")),
            summaryEnabled: () => settings.GetBool(SettingsCatalog.SummaryEnabled),
            draftFactory: (schema, executor, draftClient) => PipelineFactory.CreateDraftService(schema, executor, draftClient),
            draftEnabled: () => settings.GetBool(SettingsCatalog.EmailDraftEnabled),
            glossaryFactory: schema => glossaryService = new CrmNlQuery.Application.Glossary.Services.GlossaryService(glossaryStore, schema, () => settings.GetBool(SettingsCatalog.GlossaryEnabled),
                (actor, action, target, detail, token) => accessStore.AddAuditAsync(new CrmNlQuery.Domain.Access.Entities.AuditEntry(DateTimeOffset.UtcNow, actor, action, target, detail), token), loggerFactory.CreateLogger("CrmNlQuery.Glossary")),
            metadataTtl: () => TimeSpan.FromMinutes(Math.Max(1, settings.GetInt(SettingsCatalog.MetadataCacheMinutes))),
            refreshSchemaWhenUnknown: true,
            exampleQuestions: ExampleQuestions.Clean(configuration.GetSection("Widget:ExampleQuestions").Get<string[]>()),
            exampleQuestionsTurkish: ExampleQuestions.Clean(configuration.GetSection("Widget:ExampleQuestionsTr").Get<string[]>()));
        logger.LogInformation("Starting: model {Provider} / {Model}, identity mode {Mode}, store {Store}",
            llmProvider, llmModel, configuration["Identity:Mode"] ?? "(not set)", string.IsNullOrWhiteSpace(connection) ? "memory" : "PostgreSQL");
        if (string.IsNullOrWhiteSpace(connection))
        {
            logger.LogWarning("History:ConnectionString is not set; history and usage metrics are kept in memory only and are lost on restart.");
        }
        var metered = new MeteredAsk((request, ct) => pipeline.RunAsync(request, ct), metricsStore);
        var guard = new AskGuard(new AskGate(limits), limits, metricsStore, loggerFactory.CreateLogger<AskGuard>());

        var identityOptions = configuration.GetSection("Identity").Get<IdentityOptions>() ?? new IdentityOptions();
        IdentityGuard.Validate(identityOptions, environmentName);
        var callerResolver = PipelineFactory.CreateCallerResolver(crmOptions);
        var identity = new CallerIdentityService(identityOptions, callerResolver);
        var authOptions = configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        AuthTokenService? authTokens = null;
        CrmLoginService? crmLogin = null;
        if (string.Equals((identityOptions.Mode ?? "").Trim(), "CrmProof", StringComparison.OrdinalIgnoreCase))
        {
            authTokens = new AuthTokenService(authOptions.SigningKey);
            var authCrm = CrmClient.Create(crmOptions);
            crmLogin = new CrmLoginService(await PipelineFactory.CreateAuthChallengeStoreAsync(connection), new CrmLoginProofReader(authCrm), new CrmUserDirectory(authCrm), authTokens, authOptions);
        }

        var summaryBuilder = PipelineFactory.CreateSummaryBuilder(crmOptions, metadata, accessLogStore, () => TimeSpan.FromSeconds(settings.GetInt(SettingsCatalog.SummarySectionTimeoutSeconds)), loggerFactory.CreateLogger("CrmNlQuery.Summary"));
        var resultExecutor = PipelineFactory.CreateExecutor(crmOptions, accessLogStore);
        var reopenDrafts = PipelineFactory.CreateDraftService(metadata, resultExecutor, llm);
        var results = new ResultService(historyStore, metadata, resultExecutor,
            () => settings.Get(SettingsCatalog.QuestionTextView) == "on", summaryBuilder, reopenDrafts, () => settings.GetBool(SettingsCatalog.EmailDraftEnabled));
        var conversation = new ConversationContext(historyStore, () => TimeSpan.FromMinutes(settings.GetInt(SettingsCatalog.ConversationContextMinutes)));
        var chats = new ChatService(historyStore);
        var resultCounts = new ResultCountService(historyStore, new CountQueryBuilder(new PlanValidator(metadata, limits), metadata), PipelineFactory.CreateRecordCounter(crmOptions, accessLogStore),
            () => TimeSpan.FromSeconds(settings.GetInt(SettingsCatalog.CountTimeoutSeconds)));
        var explainer = new QueryExplainer(metadata);
        var explainEdit = new ExplainEditService(historyStore, explainer, new PlanValidator(metadata, limits), results, chats);
        var designClient = new MeteredLlmClient(localStep("design"),
            metricsStore, "design", llmProvider, llmModel, logger: loggerFactory.CreateLogger("CrmNlQuery.ModelCalls"));
        var createClient = new MeteredLlmClient(localStep("create"),
            metricsStore, "create", llmProvider, llmModel, logger: loggerFactory.CreateLogger("CrmNlQuery.ModelCalls"));
        var recordCreation = PipelineFactory.CreateRecordCreation(crmOptions, metadata, accessLogStore, createClient, dataPolicy,
            (entry, token) => accessStore.AddAuditAsync(entry, token),
            () => settings.GetBool(SettingsCatalog.RecordCreateEnabled),
            () => settings.Get(SettingsCatalog.CreateTables).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            () => settings.GetInt(SettingsCatalog.CreateMaxRows), () => settings.GetInt(SettingsCatalog.CreatePerDay),
            loggerFactory.CreateLogger("CrmNlQuery.Create"),
            () => settings.Get(SettingsCatalog.CreateHiddenFields).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var designPlanner = new CrmNlQuery.Application.Design.Services.DesignPlanner(designClient, ((PolicyMetadataSource)metadata).Unfiltered,
            async ct => { var snapshot = await dataPolicy.GetAsync(ct); return name => !snapshot.IsTableOpen(name); });
        var designJobs = new CrmNlQuery.Application.Design.Services.DesignJobService(designJobStore, designPlanner,
            () => TimeSpan.FromMinutes(settings.GetInt(SettingsCatalog.DesignTimeoutMinutes)), loggerFactory.CreateLogger("CrmNlQuery.Design"));
        var recovered = await designJobs.RecoverAsync();
        if (recovered > 0)
        {
            logger.LogWarning("{Count} customization design job(s) were interrupted by the restart and marked failed.", recovered);
        }
        var suggestions = new CrmNlQuery.Application.Suggestions.Services.SuggestionService(historyStore, metadata, new PlanValidator(metadata, limits), results, chats, suggestionStats);
        var openInCrm = new OpenInCrmService(historyStore, new ViewBuilder(new PlanValidator(metadata, limits), metadata), PipelineFactory.CreateUserViews(crmOptions),
            () => settings.GetBool(SettingsCatalog.OpenInCrm), crmOptions.BaseUrl, summaryBuilder);
        var refine = new RefineService(conversation, results, (request, ct) => metered.RunAsync(request, ct));

        var bootstrapAccounts = configuration.GetSection("Admin:Bootstrap").Get<string[]>() ?? new string[0];
        var accessService = new AccessService(accessStore, bootstrapAccounts);
        var qualityMetrics = new CrmNlQuery.Infrastructure.Persistence.Memory.Observability.MemoryMetricsStore();
        var qualityPipeline = PipelineFactory.Create(crmOptions, llm, out _,
            purpose => new MeteredLlmClient(localStep(purpose),
                qualityMetrics, purpose, llmProvider, llmModel, logger: loggerFactory.CreateLogger("CrmNlQuery.ModelCalls")),
            loggerFactory.CreateLogger<AskPipeline>(),
            knowledge: knowledgeSource,
            limits: limits,
            accessLog: accessLogStore,
            pruneContext: configuration.GetValue("Planning:PruneContext", true),
            dataPolicy: dataPolicy,
            metadataTtl: () => TimeSpan.FromMinutes(Math.Max(1, settings.GetInt(SettingsCatalog.MetadataCacheMinutes))));
        var qualityRuns = new CrmNlQuery.Application.Quality.Runs.QualityRunService(qualityStore, qualityRunStore,
            new CrmNlQuery.Application.Quality.Runs.PipelineQualityRunner(qualityPipeline), resultExecutor, metadata,
            () => TimeSpan.FromMinutes(settings.GetInt(SettingsCatalog.QualityRunTimeoutMinutes)),
            (actor, action, target, detail, token) => accessService.RecordAsync(actor, action, target, detail, token), loggerFactory.CreateLogger("CrmNlQuery.Quality"));
        var qualityScheduler = new CrmNlQuery.Application.Quality.Runs.QualityScheduler(qualityRuns, qualityRunStore,
            () => new CrmNlQuery.Application.Quality.Runs.QualitySchedule(settings.GetBool(SettingsCatalog.QualityScheduleEnabled), settings.GetInt(SettingsCatalog.QualityScheduleHour),
                settings.GetInt(SettingsCatalog.QualityScheduleRepeat), settings.Get(SettingsCatalog.QualityScheduleRunAs)),
            (account, token) => identity.GetCallerAsync(account, token), loggerFactory.CreateLogger("CrmNlQuery.Quality"));
        var interruptedRuns = await qualityRuns.RecoverAsync();
        if (interruptedRuns > 0)
        {
            logger.LogWarning("{Count} quality check run(s) were interrupted by the restart and marked failed.", interruptedRuns);
        }
        var feedback = new FeedbackService(feedbackStore, historyStore, explainer, accessService,
            () => settings.GetBool(SettingsCatalog.FeedbackEnabled), () => settings.GetBool(SettingsCatalog.FeedbackAllowSharing));
        await accessService.EnsureBootstrapAsync();
        if (!await accessService.HasAdministratorAsync())
        {
            logger.LogWarning("No administrator exists and Admin:Bootstrap is empty: nobody can open the admin panel. Set Admin:Bootstrap to a Windows account (DOMAIN\\user).");
        }

        var adminCrm = CrmClient.Create(crmOptions);
        var prices = configuration.GetSection("Pricing:Models").Get<Dictionary<string, ModelPrice>>();
        var adminService = new AdminService(metricsStore, new CrmUserNames(adminCrm), prices);
        var health = new HealthService(new List<HealthCheck>
        {
            new HealthCheck("CRM", async ct => { using var _ = await adminCrm.GetAsServiceAsync("WhoAmI", ct); }),
            new HealthCheck("Database", ct => metricsStore.GetSummaryAsync(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, ct)),
            new HealthCheck($"Model ({llmProvider} / {llmModel})", async ct =>
            {
                if (!LlmClientFactory.IsCloud(llmSelection))
                {
                    var isVllm = LlmClientFactory.IsVllm(llmSelection);
                    using var http = new HttpClient();
                    using var ping = new HttpRequestMessage(HttpMethod.Get, isVllm ? vllmOptions.BaseUrl.TrimEnd('/') + "/v1/models" : ollamaOptions.BaseUrl.TrimEnd('/') + "/api/tags");
                    if (isVllm && !string.IsNullOrWhiteSpace(vllmOptions.ApiKey))
                    {
                        ping.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", vllmOptions.ApiKey);
                    }
                    using var response = await http.SendAsync(ping, ct);
                    response.EnsureSuccessStatusCode();
                }
            }),
        }, TimeSpan.FromSeconds(5));

        return new AppServices
        {
            CrmBaseUrl = crmOptions.BaseUrl,
            Ask = metered,
            Create = recordCreation,
            Results = results,
            Refine = refine,
            Conversation = conversation,
            RecordContext = new RecordContextResolver(metadata, PipelineFactory.CreateExecutor(crmOptions, accessLogStore)),
            AccessLog = accessLogStore,
            AccessLogs = new AccessLogService(accessLogStore, new CrmUserNames(adminCrm)),
            ExampleQuestions = ExampleQuestions.Clean(configuration.GetSection("Widget:ExampleQuestions").Get<string[]>()),
            ExampleQuestionsTurkish = ExampleQuestions.Clean(configuration.GetSection("Widget:ExampleQuestionsTr").Get<string[]>()),
            QuestionViews = new QuestionViewService(historyStore, callerResolver, accessService, () => settings.Get(SettingsCatalog.QuestionTextView) == "on"),
            SecurityLogRetentionDays = () => settings.GetInt(SettingsCatalog.SecurityLogRetentionDays),
            Chats = chats,
            OpenInCrm = openInCrm,
            Explainer = explainer,
            DataPolicyStore = dataPolicyStore,
            DataPolicy = dataPolicy,
            DataPolicyAdmin = new DataPolicyAdminService(((PolicyMetadataSource)metadata).Unfiltered, dataPolicyStore, dataPolicy, PipelineFactory.CreateExecutor(crmOptions, accessLogStore),
                (actor, action, target, detail, token) => accessService.RecordAsync(actor, action, target, detail, token)),
            ExplainEdit = explainEdit,
            Suggestions = suggestions,
            DesignJobs = designJobs,
            Glossary = new CrmNlQuery.Application.Glossary.Services.GlossaryAdminService(glossaryService!, glossaryStore, metadata, resultExecutor, () => settings.GetInt(SettingsCatalog.GlossaryMaxEntries),
                (actor, action, target, detail, token) => accessService.RecordAsync(actor, action, target, detail, token),
                async token => (await knowledgeStore.ListAsync(token)).Where(d => d.Enabled).Select(d => (d.Title, d.Body)).ToList()),
            Quality = new CrmNlQuery.Application.Quality.Services.QualityService(qualityStore, metadata, resultExecutor, () => settings.GetBool(SettingsCatalog.QualityEnabled),
                () => settings.GetInt(SettingsCatalog.QualityMaxCases), (actor, action, target, detail, token) => accessService.RecordAsync(actor, action, target, detail, token), history: historyStore,
                suggestionsAllowed: () => settings.GetBool(SettingsCatalog.QualityAllowSuggestions)),
            QualityRuns = qualityRuns,
            QualityScheduler = qualityScheduler,
            Reports = new CrmNlQuery.Application.Reports.Services.ReportSearchService(resultExecutor, metadata, () => settings.GetBool(SettingsCatalog.ReportsEnabled), crmOptions.BaseUrl,
                reportCatalogStore, new CrmObjectTypeCodes(adminCrm), loggerFactory.CreateLogger("CrmNlQuery.Reports")),
            ReportCatalog = new CrmNlQuery.Application.Reports.Services.ReportCatalogService(reportCatalogStore, () => settings.GetInt(SettingsCatalog.ReportsMaxEntries),
                (actor, action, target, detail, token) => accessService.RecordAsync(actor, action, target, detail, token)),
            QualityRunStore = qualityRunStore,
            QualityRunRetentionDays = () => settings.GetInt(SettingsCatalog.QualityRunRetentionDays),
            DesignJobStore = designJobStore,
            SuggestionStats = suggestionStats,
            ReportStats = reportStats,
            Export = new CrmNlQuery.Application.Export.Services.ExportService(accessStore as CrmNlQuery.Application.Export.Abstractions.IAuditPager, accessLogStore as CrmNlQuery.Application.Export.Abstractions.IAccessLogPager,
                new CrmUserNames(adminCrm), metricsStore, feedbackStore, accessService, () => settings.GetInt(SettingsCatalog.ExportMaxRows),
                questions: historyStore as CrmNlQuery.Application.Export.Abstractions.IQuestionPager, questionTextEnabled: () => settings.Get(SettingsCatalog.QuestionTextView) == "on"),
            Feedback = feedback,
            FeedbackStore = feedbackStore,
            FeedbackRetentionDays = () => settings.GetInt(SettingsCatalog.FeedbackRetentionDays),
            DesignRetentionDays = () => settings.GetInt(SettingsCatalog.DesignRetentionDays),
            ShowFetchXml = () => settings.GetBool(SettingsCatalog.ShowFetchXml),
            ResultCounts = resultCounts,
            Preferences = preferences,
            Language = new CrmNlQuery.Application.Localization.Services.LanguageResolver(preferences, () => settings.Get(SettingsCatalog.UiDefaultLanguage)),
            ChatTurns = new ChatTurnService(chats, conversation, results),
            Identity = identity,
            CrmLogin = crmLogin,
            AuthTokens = authTokens,
            IdentityOptions = identityOptions,
            Access = accessService,
            AppUse = new AppUseGate(accessService, () => settings.Get(SettingsCatalog.AccessRequireAppUse), loggerFactory.CreateLogger("CrmNlQuery.Access")),
            Knowledge = knowledge,
            Guard = guard,
            Settings = settings,
            Admin = adminService,
            Health = health,
            LogStore = logStore,
            Metadata = metadata,
            MetricsStore = metricsStore,
            HistoryStore = historyStore,
            MetricsRetentionDays = () => settings.GetInt(SettingsCatalog.MetricsRetentionDays),
            HistoryRetentionDays = () => settings.GetInt(SettingsCatalog.HistoryRetentionDays),
        };
    }

    private static async Task WaitForDatabaseAsync(string connection, IConfiguration configuration, ILogger logger, CancellationToken ct)
    {
        var wait = TimeSpan.FromSeconds(ReadSeconds(configuration["Startup:DatabaseWaitSeconds"], 90));
        var interval = TimeSpan.FromSeconds(Math.Max(1, ReadSeconds(configuration["Startup:DatabaseRetrySeconds"], 5)));
        var probe = PostgresReadiness.CreateProbe(connection);
        await StartupRetry.WaitAsync("PostgreSQL", probe, wait, interval,
            onRetry: (attempt, ex, next) => logger.LogWarning(
                "PostgreSQL is not reachable yet (attempt {Attempt}: {ExceptionType}: {Message}). Retrying in {Seconds} s; the app starts as soon as it is up (waits at most {MaxSeconds} s, setting Startup:DatabaseWaitSeconds).",
                attempt, ex.GetType().Name, ex.Message, (int)next.TotalSeconds, (int)wait.TotalSeconds),
            isTransient: PostgresReadiness.IsTransient, ct: ct);
    }

    private static int ReadSeconds(string? value, int fallback)
        => int.TryParse(value, out var seconds) && seconds >= 0 ? seconds : fallback;
}
