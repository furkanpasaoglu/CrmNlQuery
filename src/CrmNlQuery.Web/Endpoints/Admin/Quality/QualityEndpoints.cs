using CrmNlQuery.Application.Quality.Runs;
using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Web.Contracts.Admin.Quality;
using CrmNlQuery.Application.Operations.Settings;

namespace CrmNlQuery.Web.Endpoints.Admin.Quality;

/// <summary>
/// Maps the HTTP endpoints for quality.
/// </summary>
public static class QualityEndpoints
{
    private static async Task<object> ScheduleViewAsync(AppServices services, CancellationToken ct)
    {
        var scheduler = services.QualityScheduler;
        var schedule = new QualitySchedule(services.Settings.GetBool(SettingsCatalog.QualityScheduleEnabled), services.Settings.GetInt(SettingsCatalog.QualityScheduleHour),
            services.Settings.GetInt(SettingsCatalog.QualityScheduleRepeat), services.Settings.Get(SettingsCatalog.QualityScheduleRunAs));
        var now = scheduler.Now();
        var ranToday = await scheduler.RanTodayAsync(now, ct);
        var next = scheduler.NextRun(now, schedule, ranToday);
        return new
        {
            enabled = schedule.Enabled,
            hour = schedule.Hour,
            repeat = schedule.Repeat,
            runAs = schedule.RunAs,
            windowHours = QualityScheduler.WindowHours,
            serverTime = now.ToString("yyyy-MM-dd HH:mm"),
            nextRun = next?.ToString("yyyy-MM-dd HH:mm"),
            lastRun = await scheduler.LastRunAsync(ct),
        };
    }

    /// <summary>
    /// Maps the acceptance-set (quality check) admin endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        var quality = services.Quality;

        app.MapGet("/api/admin/quality/cases", async (string? status, string? source, bool? verified, string? entity, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            var cases = await quality.ListAsync(new QualityCaseFilter(Blank(status), Blank(source), verified, Blank(entity)), ct);
            var showFetch = services.ShowFetchXml();
            return Results.Json(new
            {
                persistent = quality.Persistent,
                maxCases = quality.MaxCases,
                showFetchXml = showFetch,
                cases = cases.Select(c => new
                {
                    id = c.Id,
                    question = c.Question,
                    entity = c.Entity,
                    source = c.Source,
                    status = c.Status,
                    verified = c.Verified,
                    template = c.Template,
                    note = c.Note,
                    language = c.Language,
                    createdBy = c.CreatedBy,
                    createdAt = c.CreatedAt,
                    referenceFetch = showFetch ? c.ReferenceFetch : null,
                }),
            });
        }).RequireAdmin(Permissions.QualityManage);

        app.MapGet("/api/admin/quality/entities", async (CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            var list = await quality.CandidatesAsync(ct);
            return Results.Json(list.Select(c => new { entity = c.Entity, label = c.Label }));
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPost("/api/admin/quality/generate", async (QualityGenerateDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            if (string.IsNullOrWhiteSpace(dto.Entity)) { return Results.BadRequest(new { error = "entity is required" }); }
            try
            {
                var language = string.IsNullOrWhiteSpace(dto.Language) ? RequestLanguage.Current : dto.Language;
                var result = await quality.GenerateAsync(dto.Entity, language, http.Admin().Account, http.Caller(), ct);
                return Results.Json(new { added = result.Added, duplicates = result.Duplicates, reason = result.Reason });
            }
            catch (CrmNlQuery.Application.Common.Exceptions.CrmException ex)
            {
                return Results.Json(new { error = UserMessages.ForCrm(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAdmin(Permissions.QualityManage).RequireCaller();

        app.MapPost("/api/admin/quality/approve", async (QualityIdsDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return Results.Json(new { changed = await quality.ApproveAsync(Ids(dto), http.Admin().Account, ct) });
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPost("/api/admin/quality/retire", async (QualityIdsDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return Results.Json(new { changed = await quality.RetireAsync(Ids(dto), true, http.Admin().Account, ct) });
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPost("/api/admin/quality/restore", async (QualityIdsDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return Results.Json(new { changed = await quality.RetireAsync(Ids(dto), false, http.Admin().Account, ct) });
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPost("/api/admin/quality/delete", async (QualityIdsDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return Results.Json(new { changed = await quality.DeleteAsync(Ids(dto), http.Admin().Account, ct) });
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPost("/api/admin/quality/runs", async (QualityRunDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            try
            {
                var run = await services.QualityRuns.StartAsync(http.Admin().Account, http.Caller(), dto.Repeat ?? 1, ct);
                return Results.Json(RunView(run), statusCode: StatusCodes.Status202Accepted);
            }
            catch (QualityRunException ex)
            {
                var (status, key) = ex.Code switch
                {
                    "busy" => (StatusCodes.Status409Conflict, Msg.QualityRunBusy),
                    "empty" => (StatusCodes.Status409Conflict, Msg.QualityRunEmpty),
                    _ => (StatusCodes.Status400BadRequest, Msg.QualityRunInvalid),
                };
                return Results.Json(new { error = MessageCatalog.Now(key), code = ex.Code }, statusCode: status);
            }
        }).RequireAdmin(Permissions.QualityManage).RequireCaller();

        app.MapGet("/api/admin/quality/schedule", async (CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return Results.Json(await ScheduleViewAsync(services, ct));
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPut("/api/admin/quality/schedule", async (QualityScheduleDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            var hour = dto.Hour ?? services.Settings.GetInt(SettingsCatalog.QualityScheduleHour);
            var repeat = dto.Repeat ?? services.Settings.GetInt(SettingsCatalog.QualityScheduleRepeat);
            if (hour < 0 || hour > 23 || repeat < 1 || repeat > QualityRunService.MaxRepeat)
            {
                return Results.Json(new { error = MessageCatalog.Now(Msg.QualityScheduleInvalid) }, statusCode: StatusCodes.Status400BadRequest);
            }
            var actor = http.Admin()!.Account;
            var enabled = dto.Enabled ?? services.Settings.GetBool(SettingsCatalog.QualityScheduleEnabled);
            foreach (var (key, value) in new[]
            {
                (SettingsCatalog.QualityScheduleHour, hour.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (SettingsCatalog.QualityScheduleRepeat, repeat.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (SettingsCatalog.QualityScheduleRunAs, actor),
                (SettingsCatalog.QualityScheduleEnabled, enabled ? "true" : "false"),
            })
            {
                var saved = await services.Settings.SetAsync(actor, key, value, ct);
                if (!saved.Ok) { return Results.Json(new { error = saved.Message }, statusCode: StatusCodes.Status400BadRequest); }
            }
            await services.Access.RecordAsync(actor, "quality.schedule", "schedule", $"{(enabled ? "on" : "off")}, hour {hour}, repeat {repeat}", ct);
            return Results.Json(await ScheduleViewAsync(services, ct));
        }).RequireAdmin(Permissions.QualityManage);

        app.MapGet("/api/admin/quality/runs", async (CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return Results.Json((await services.QualityRuns.ListAsync(ct)).Select(RunView));
        }).RequireAdmin(Permissions.QualityManage);

        app.MapGet("/api/admin/quality/runs/{id}", async (string id, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            var report = await services.QualityRuns.ReportAsync(id, ct);
            if (report is null) { return Results.NotFound(); }
            return Results.Json(new { run = RunView(report.Value.Run), report = report.Value.Report });
        }).RequireAdmin(Permissions.QualityManage);

        app.MapPost("/api/admin/quality/runs/{id}/cancel", (string id) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            return services.QualityRuns.Cancel(id) ? Results.Json(new { cancelling = true }) : Results.NotFound();
        }).RequireAdmin(Permissions.QualityManage);

        app.MapGet("/api/admin/quality/export", async (HttpContext http, CancellationToken ct) =>
        {
            if (!quality.Enabled) { return Results.NotFound(); }
            http.Response.Headers["Content-Disposition"] = "attachment; filename=\"acceptance-set.json\"";
            http.Response.Headers["Cache-Control"] = "no-store";
            return Results.Text(await quality.ExportAsync(ct), "application/json; charset=utf-8");
        }).RequireAdmin(Permissions.QualityManage);
    }

    private static object RunView(QualityRun r) => new
    {
        id = r.Id, startedBy = r.StartedBy, startedAt = r.StartedAt, finishedAt = r.FinishedAt, status = r.Status, repeat = r.Repeat, total = r.Total, done = r.Done,
        summary = string.IsNullOrEmpty(r.SummaryJson) ? (object?)null : System.Text.Json.JsonDocument.Parse(r.SummaryJson).RootElement.Clone(), error = r.Error,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyCollection<string> Ids(QualityIdsDto dto) => (dto.Ids ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).Take(500).ToList();
}
