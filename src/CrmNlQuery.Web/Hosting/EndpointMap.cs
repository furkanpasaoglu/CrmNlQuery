using CrmNlQuery.Composition;
using CrmNlQuery.Web.Endpoints.Admin;
using CrmNlQuery.Web.Endpoints.Admin.Data;
using CrmNlQuery.Web.Endpoints.Admin.Quality;
using CrmNlQuery.Web.Endpoints.Admin.Tools;
using CrmNlQuery.Web.Endpoints.Ask;
using CrmNlQuery.Web.Endpoints.Auth;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Maps all HTTP endpoints once the services are ready.
/// </summary>
public static class EndpointMap
{
    /// <summary>
    /// Registers every endpoint on the route builder.
    /// </summary>
    public static void MapAll(IEndpointRouteBuilder app, AppServices services, bool testPanelEnabled, IReadOnlyList<string> allowedOrigins)
    {
        AuthEndpoints.Map(app, services, allowedOrigins);
        AskEndpoints.Map(app, services);
        ChatEndpoints.Map(app, services);
        CreateEndpoints.Map(app, services);
        PreferencesEndpoints.Map(app, services);
        FeedbackEndpoints.Map(app, services);
        QualitySaveEndpoints.Map(app, services);

        AdminEndpoints.Map(app, services.Access, services.Admin, services.Health, services.LogStore, services.Knowledge, services.Settings, services.IdentityOptions, services.AccessLogs, services.QuestionViews,
            appHome: testPanelEnabled ? "index.html" : "", refreshMetadata: ct => services.Metadata.RefreshAsync(ct));
        DataPolicyEndpoints.Map(app, services);
        LanguageAdminEndpoints.Map(app, services);
        FeedbackAdminEndpoints.Map(app, services);
        ExportEndpoints.Map(app, services);
        GlossaryEndpoints.Map(app, services);
        ReportEndpoints.Map(app, services);
        QualityEndpoints.Map(app, services);
        DesignEndpoints.Map(app, services);
    }
}
