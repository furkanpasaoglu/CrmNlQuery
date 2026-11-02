using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Operations.Settings;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Application.CrmViews.Services;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.Feedback.Abstractions;
using CrmNlQuery.Application.Feedback.Services;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Application.Preferences.Services;

namespace CrmNlQuery.Composition;

/// <summary>
/// Holds the constructed objects the service needs, which the HTTP layer sees without infrastructure.
/// </summary>
public sealed class AppServices
{
    public required string CrmBaseUrl { get; init; }

    public required MeteredAsk Ask { get; init; }
    public CrmNlQuery.Application.Create.Services.CreateService? Create { get; init; }
    public required ResultService Results { get; init; }
    public required RefineService Refine { get; init; }
    public required ConversationContext Conversation { get; init; }
    public required RecordContextResolver RecordContext { get; init; }
    public required ChatService Chats { get; init; }
    public required OpenInCrmService OpenInCrm { get; init; }
    public required QueryExplainer Explainer { get; init; }
    public required IDataPolicyStore DataPolicyStore { get; init; }
    public required DataPolicyService DataPolicy { get; init; }
    public required DataPolicyAdminService DataPolicyAdmin { get; init; }
    public required ExplainEditService ExplainEdit { get; init; }
    public required CrmNlQuery.Application.Suggestions.Services.SuggestionService Suggestions { get; init; }
    public required CrmNlQuery.Application.Suggestions.Abstractions.ISuggestionStats SuggestionStats { get; init; }
    public required CrmNlQuery.Application.Export.Services.ExportService Export { get; init; }
    public required CrmNlQuery.Application.Design.Services.DesignJobService DesignJobs { get; init; }
    public required CrmNlQuery.Application.Quality.Services.QualityService Quality { get; init; }
    public required CrmNlQuery.Application.Glossary.Services.GlossaryAdminService Glossary { get; init; }
    public required CrmNlQuery.Application.Quality.Runs.QualityRunService QualityRuns { get; init; }
    public required CrmNlQuery.Application.Quality.Runs.QualityScheduler QualityScheduler { get; init; }
    public required CrmNlQuery.Application.Reports.Services.ReportSearchService Reports { get; init; }
    public required CrmNlQuery.Application.Reports.Services.ReportCatalogService ReportCatalog { get; init; }
    public required CrmNlQuery.Application.Reports.Abstractions.IReportStats ReportStats { get; init; }
    internal CrmNlQuery.Application.Quality.Abstractions.IQualityRunStore QualityRunStore { get; init; } = null!;
    internal Func<int> QualityRunRetentionDays { get; init; } = () => 180;
    internal CrmNlQuery.Application.Design.Abstractions.IDesignJobStore DesignJobStore { get; init; } = null!;
    internal Func<int> DesignRetentionDays { get; init; } = () => 90;
    public required FeedbackService Feedback { get; init; }
    public required Func<bool> ShowFetchXml { get; init; }
    public required ResultCountService ResultCounts { get; init; }
    public IReadOnlyList<string> ExampleQuestions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExampleQuestionsTurkish { get; init; } = Array.Empty<string>();
    public required PreferencesService Preferences { get; init; }
    public required CrmNlQuery.Application.Localization.Services.LanguageResolver Language { get; init; }
    public required ChatTurnService ChatTurns { get; init; }

    public required CallerIdentityService Identity { get; init; }
    public CrmNlQuery.Application.Access.Services.CrmLoginService? CrmLogin { get; init; }
    public CrmNlQuery.Application.Access.Services.AuthTokenService? AuthTokens { get; init; }
    public required IdentityOptions IdentityOptions { get; init; }

    public required AccessService Access { get; init; }
    public required AppUseGate AppUse { get; init; }
    public required IAccessLogStore AccessLog { get; init; }
    public required AccessLogService AccessLogs { get; init; }
    public required QuestionViewService QuestionViews { get; init; }
    public required AdminService Admin { get; init; }
    public required HealthService Health { get; init; }
    public required ILogStore LogStore { get; init; }

    public required KnowledgeService Knowledge { get; init; }

    public required AskGuard Guard { get; init; }
    public required RuntimeSettings Settings { get; init; }

    public required IMetadataSource Metadata { get; init; }

    internal IMetricsStore MetricsStore { get; init; } = null!;
    internal IHistoryStore HistoryStore { get; init; } = null!;
    internal Func<int> MetricsRetentionDays { get; init; } = () => 90;
    internal Func<int> HistoryRetentionDays { get; init; } = () => 90;
    internal Func<int> SecurityLogRetentionDays { get; init; } = () => 365;
    internal IFeedbackStore FeedbackStore { get; init; } = null!;
    internal Func<int> FeedbackRetentionDays { get; init; } = () => 180;

    /// <summary>
    /// Starts a background job that deletes old metrics, logs and history once a day.
    /// </summary>
    public void StartRetention(ILogger logger, CancellationToken stopping)
    {
        _ = Settings.RunRefreshLoopAsync(stopping);
        _ = QualityScheduler.RunLoopAsync(stopping);
        _ = MetricsRetention.RunAsync(MetricsStore, MetricsRetentionDays,
            ex => logger.LogWarning(ex, "Metrics cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => LogStore.PurgeOlderThanAsync(cutoff, token), MetricsRetentionDays,
            ex => logger.LogWarning(ex, "Log cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => HistoryStore.PurgeOlderThanAsync(cutoff, token), HistoryRetentionDays,
            ex => logger.LogWarning(ex, "History cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => FeedbackStore.PurgeAsync(cutoff, null, token), MetricsRetentionDays,
            ex => logger.LogWarning(ex, "Feedback cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => FeedbackStore.PurgeAsync(null, cutoff, token), FeedbackRetentionDays,
            ex => logger.LogWarning(ex, "Feedback candidate cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => SuggestionStats.PurgeOlderThanAsync(cutoff, token), MetricsRetentionDays,
            ex => logger.LogWarning(ex, "Suggestion stats cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => ReportStats.PurgeOlderThanAsync(cutoff, token), MetricsRetentionDays,
            ex => logger.LogWarning(ex, "Report stats cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => DesignJobStore.PurgeOlderThanAsync(cutoff, token), DesignRetentionDays,
            ex => logger.LogWarning(ex, "Design job cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => QualityRunStore.PurgeOlderThanAsync(cutoff, token), QualityRunRetentionDays,
            ex => logger.LogWarning(ex, "Quality run cleanup failed"), stopping);
        _ = MetricsRetention.RunAsync((cutoff, token) => AccessLog.PurgeOlderThanAsync(cutoff, token), SecurityLogRetentionDays,
            ex => logger.LogWarning(ex, "Access log cleanup failed"), stopping);
    }
}
