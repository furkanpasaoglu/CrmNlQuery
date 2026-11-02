using CrmNlQuery.Application.Operations.Settings;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.History.Models;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Common.Constants;
using CrmNlQuery.Web.Endpoints.Admin.Data;
using CrmNlQuery.Application.Common.Exceptions;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Web.Endpoints.Admin;

/// <summary>
/// Maps the admin panel endpoints under /api/admin, each verifying permission on the server first.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Maps all admin panel endpoints onto the route builder with the given services.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AccessService access, AdminService report, HealthService health, ILogStore logs,
        KnowledgeService knowledge, RuntimeSettings settings, IdentityOptions identityOptions, AccessLogService accessLogs, QuestionViewService questionViews, string appHome, Func<CancellationToken, Task> refreshMetadata)
    {
        app.MapGet("/api/admin/me", async (HttpContext http, CancellationToken ct) =>
        {
            var principal = http.Admin();
            return Results.Json(new
            {
                account = principal!.Account,
                name = principal.DisplayName,
                roles = principal.RoleNames,
                isAdministrator = principal.IsAdministrator,
                language = RequestLanguage.Current,
                languages = UiLanguage.All,
                appHome = appHome,
                permissions = principal.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            });
        }).RequireAdmin(null);

        app.MapGet("/api/admin/identity", async (HttpContext http, CancellationToken ct) =>
        {
            var me = http.Admin();
            var isMember = GroupMembership.Create(identityOptions, http.User);
            var mapped = (await access.ListGroupMappingsAsync(ct)).Where(m => isMember(m.DisplayName)).Select(m => m.DisplayName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return Results.Json(new
            {
                account = me.Account,
                identityMode = identityOptions.Mode,
                groups = TokenGroups.Names(identityOptions, http.User),
                memberOfMappedGroups = mapped,
                roles = me.RoleNames,
                permissions = me.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
                isAdministrator = me.IsAdministrator,
                appUseMode = settings.Get(SettingsCatalog.AccessRequireAppUse),
                hasAppUse = me.Has(Permissions.AppUse),
            });
        }).RequireAdmin(null);

        app.MapGet("/api/admin/access-log", async (HttpContext http, int? hours, Guid? user, string? table, int? take, CancellationToken ct) =>
        {
            var actor = http.Admin();
            var to = DateTimeOffset.UtcNow.AddMinutes(1);
            var span = Math.Clamp(hours ?? 24, 1, 8760);
            var from = to.AddHours(-span).AddMinutes(-1);
            var tableFilter = string.IsNullOrWhiteSpace(table) ? null : table.Trim().ToLowerInvariant();
            var rows = await accessLogs.ListAsync(new DataAccessQuery(from, to, user, tableFilter, Math.Clamp(take ?? 200, 1, 500)), ct);
            try
            {
                var filters = $"last {span}h; user {(user?.ToString() ?? "all")}; table {(tableFilter ?? "all")}";
                await access.RecordViewAsync("accesslog.view", actor.Account, "GET /api/admin/access-log", filters, $"{filters}; {rows.Count} rows", rateLimit: true, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AuditLogger(app).LogWarning(ex, "Could not write the accesslog.view audit record.");
            }
            return Results.Json(rows.Select(r => new
            {
                id = r.Id,
                at = r.At,
                user = r.User,
                userName = r.UserName,
                source = r.Source,
                tables = r.Tables,
                rows = r.Rows,
                outcome = r.Outcome,
                requestId = r.RequestId,
            }));
        }).RequireAdmin(Permissions.AccessLogView);

        app.MapGet("/api/admin/users/questions", async (HttpContext http, Guid? user, string? account, int? take, CancellationToken ct) =>
        {
            var actor = http.Admin();
            if (!questionViews.Enabled)
            {
                return Results.NotFound();
            }
            if (user is null && string.IsNullOrWhiteSpace(account))
            {
                return Results.BadRequest(new { error = MessageCatalog.Now(Msg.AdmGiveUserOrAccount) });
            }
            QuestionViewResult result;
            try
            {
                result = user is not null
                    ? await questionViews.ListByIdAsync(actor.Account, user.Value, take ?? 50, ct)
                    : await questionViews.ListByAccountAsync(actor.Account, account!, take ?? 50, ct);
            }
            catch (CrmException ex)
            {
                return Results.Json(new { error = UserMessages.ForCrm(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return result.Status switch
            {
                QuestionViewStatus.NotEnabled => Results.NotFound(),
                QuestionViewStatus.NotFound => Results.Json(new { error = MessageCatalog.Now(Msg.AdmAccountNotFound) }, statusCode: StatusCodes.Status404NotFound),
                _ => Results.Json(new { questions = result.Questions.Select(q => new { at = q.At, question = q.Question, entity = q.Entity }) }),
            };
        }).RequireAdmin(Permissions.QuestionsView);

        app.MapGet("/api/admin/report", async (HttpContext http, int? days, CancellationToken ct) =>
        {
            return Results.Json(await report.ReportAsync(days ?? 7, ct));
        }).RequireAdmin(Permissions.DashboardView);

        app.MapGet("/api/admin/health", async (HttpContext http, CancellationToken ct) =>
        {
            return Results.Json(await health.CheckAsync(ct));
        }).RequireAdmin(Permissions.DashboardView);

        app.MapPost("/api/admin/metadata/refresh", async (HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin().Account;
            try
            {
                await refreshMetadata(ct);
                await RecordRefreshAsync(app, access, actor, "ok", ct);
                return Results.Ok();
            }
            catch (CrmException ex)
            {
                await RecordRefreshAsync(app, access, actor, "failed", ct);
                return Results.Json(new { error = UserMessages.ForCrm(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAdmin(Permissions.MetadataRefresh);

        app.MapGet("/api/admin/logs", async (HttpContext http, string? level, string? q, int? hours, int? take, CancellationToken ct) =>
        {
            var from = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours ?? 24, 1, 2160));
            var query = new LogQuery(LogLevels.Parse(level, LogLevels.Warning), string.IsNullOrWhiteSpace(q) ? null : q.Trim(), from, Math.Clamp(take ?? 200, 1, 500));
            var entries = (await logs.QueryAsync(query, ct)).Select(e => new
            {
                at = e.At,
                level = LogLevels.Name(e.Level),
                message = e.Message,
                exception = e.Exception,
                requestId = e.RequestId,
            });
            return Results.Json(entries);
        }).RequireAdmin(Permissions.LogsView);

        app.MapGet("/api/admin/permissions", async (HttpContext http, CancellationToken ct) =>
        {
            return Results.Json(Permissions.All);
        }).RequireAdmin(Permissions.RolesManage);

        app.MapGet("/api/admin/audit", async (HttpContext http, int? take, CancellationToken ct) =>
        {
            return Results.Json(await access.ListAuditAsync(take ?? 100, ct));
        }).RequireAdmin(Permissions.AuditView);

        AccessAdminEndpoints.Map(app, access);
        DocumentEndpoints.Map(app, knowledge);
        SettingsEndpoints.Map(app, access, settings);
    }

    /// <summary>
    /// Returns the logger where warnings fall when an audit record cannot be written.
    /// </summary>
    internal static ILogger AuditLogger(IEndpointRouteBuilder app) => app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CrmNlQuery.Admin");

    private static async Task RecordRefreshAsync(IEndpointRouteBuilder app, AccessService access, string actor, string outcome, CancellationToken ct)
    {
        try
        {
            await access.RecordAsync(actor, "metadata.refresh", "crm metadata", outcome, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuditLogger(app).LogWarning(ex, "Could not write the metadata.refresh audit record.");
        }
    }

    /// <summary>
    /// Translates a rule result into an HTTP response with an error body or the created id.
    /// </summary>
    internal static IResult ToHttp(AccessResult result)
    {
        if (result.Ok)
        {
            return result.Id is null ? Results.Ok() : Results.Json(new { id = result.Id });
        }
        var status = result.Error switch
        {
            AccessError.Forbidden => StatusCodes.Status403Forbidden,
            AccessError.NotFound => StatusCodes.Status404NotFound,
            AccessError.Invalid => StatusCodes.Status400BadRequest,
            AccessError.Conflict => StatusCodes.Status409Conflict,
            AccessError.ConfirmationRequired => StatusCodes.Status409Conflict,
            AccessError.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
        return result.Error == AccessError.ConfirmationRequired
            ? Results.Json(new { error = result.Message, code = "confirm_required" }, statusCode: status)
            : Results.Json(new { error = result.Message }, statusCode: status);
    }
}
