using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Admin.Data;

namespace CrmNlQuery.Web.Endpoints.Admin.Data;

/// <summary>
/// Maps the report catalog admin endpoints: list, save, delete, list CRM reports and try, requiring Reports.Manage and auditing changes.
/// </summary>
public static class ReportEndpoints
{
    /// <summary>
    /// Maps the report catalog admin endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        var catalog = services.ReportCatalog;

        app.MapGet("/api/admin/reports", async (CancellationToken ct) =>
        {
            var entries = await catalog.ListAsync(ct);
            return Results.Json(new
            {
                enabled = services.Reports.Enabled,
                persistent = catalog.Persistent,
                maxEntries = catalog.MaxEntries,
                entries = entries.Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    description = e.Description,
                    keywords = e.Keywords,
                    crmReportId = e.CrmReportId,
                    url = e.Url,
                    recordEntity = e.RecordEntity,
                    recordParam = e.RecordParam,
                    enabled = e.Enabled,
                    updatedBy = e.UpdatedBy,
                    updatedAt = e.UpdatedAt,
                }),
            });
        }).RequireAdmin(Permissions.ReportsManage);

        app.MapGet("/api/admin/reports/summary", async (int? days, CancellationToken ct) =>
        {
            var window = Math.Clamp(days ?? 7, 1, 365);
            var stats = await services.ReportStats.SummaryAsync(window, 1000, ct);
            return Results.Json(new
            {
                days = window,
                enabled = services.Reports.Enabled,
                shown = stats.Sum(s => s.Shown),
                opened = stats.Sum(s => s.Opened),
                top = stats.Take(5).Select(s => new { name = s.Name, shown = s.Shown, opened = s.Opened }),
            });
        }).RequireAdmin(Permissions.DashboardView);

        app.MapGet("/api/admin/reports/crm", async (HttpContext http, CancellationToken ct) =>
        {
            var reports = await services.Reports.CrmReportsAsync(http.Caller(), ct);
            return Results.Json(reports.Select(r => new { id = r.Id, name = r.Name, description = r.Description }));
        }).RequireAdmin(Permissions.ReportsManage).RequireCaller();

        app.MapPost("/api/admin/reports", async (ReportCatalogDto dto, HttpContext http, CancellationToken ct) =>
            ToHttp(await catalog.SaveAsync(http.Admin().Account, ToInput(dto, null), ct))).RequireAdmin(Permissions.ReportsManage);

        app.MapPut("/api/admin/reports/{id}", async (string id, ReportCatalogDto dto, HttpContext http, CancellationToken ct) =>
            ToHttp(await catalog.SaveAsync(http.Admin().Account, ToInput(dto, id), ct))).RequireAdmin(Permissions.ReportsManage);

        app.MapDelete("/api/admin/reports/{id}", async (string id, HttpContext http, CancellationToken ct) =>
            await catalog.DeleteAsync(http.Admin().Account, id, ct) ? Results.Json(new { ok = true }) : Results.Json(new { reason = "not_found" }, statusCode: StatusCodes.Status404NotFound))
            .RequireAdmin(Permissions.ReportsManage);

        app.MapPost("/api/admin/reports/test", async (ReportTestDto dto, HttpContext http, CancellationToken ct) =>
        {
            var question = dto.Question ?? "";
            if (!ReportMatcher.Matches(question)) { return Results.Json(new { isReportQuestion = false, matches = Array.Empty<object>() }); }
            var items = await services.Reports.TestAsync(question, http.Caller(), ct);
            return Results.Json(new { isReportQuestion = true, matches = items.Select(r => new { id = r.Id, name = r.Name, description = r.Description }) });
        }).RequireAdmin(Permissions.ReportsManage).RequireCaller();
    }

    private static ReportCatalogInput ToInput(ReportCatalogDto dto, string? id)
        => new ReportCatalogInput(id, dto.Name, dto.Description, dto.Keywords, dto.CrmReportId, dto.Url, dto.RecordEntity, dto.RecordParam, dto.Enabled ?? true);

    private static IResult ToHttp(ReportCatalogResult result)
    {
        if (result.Ok)
        {
            return Results.Json(new { ok = true, id = result.Entry!.Id });
        }
        var status = result.Error == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
        return Results.Json(new { code = result.Error, reason = result.Error }, statusCode: status);
    }
}
