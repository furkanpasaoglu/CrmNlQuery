using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Web.Contracts.Admin.Data;

namespace CrmNlQuery.Web.Endpoints.Admin.Data;

/// <summary>
/// Maps the business glossary admin endpoints: list, save, enable, delete, try and JSON export and import, requiring Glossary.Manage and auditing changes.
/// </summary>
public static class GlossaryEndpoints
{
    /// <summary>
    /// Maps the glossary admin endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        var glossary = services.Glossary;

        app.MapGet("/api/admin/glossary", async (CancellationToken ct) =>
        {
            var rows = await glossary.ListAsync(ct);
            return Results.Json(new
            {
                enabled = glossary.Enabled,
                persistent = glossary.Persistent,
                maxEntries = glossary.MaxEntries,
                broken = rows.Count(r => r.Status == "broken"),
                entries = rows.Select(r => new
                {
                    id = r.Entry.Id,
                    kind = r.Entry.Kind,
                    terms = r.Entry.Terms.Select(t => new { text = t.Text, language = t.Language }),
                    entity = r.Entry.Entity,
                    attribute = r.Entry.Attribute,
                    conditions = r.Entry.Conditions.Select(c => new { attribute = c.Attribute, @operator = c.Operator, values = c.Values }),
                    description = r.Entry.Description,
                    enabled = r.Entry.Enabled,
                    status = r.Status,
                    reason = r.Reason,
                    applied = r.Applied,
                    notApplied = r.NotApplied,
                    lastNotAppliedAt = r.LastNotAppliedAt,
                    modifiedBy = r.Entry.ModifiedBy,
                    modifiedAt = r.Entry.ModifiedAt,
                }),
            });
        }).RequireAdmin(Permissions.GlossaryManage);

        app.MapGet("/api/admin/glossary/summary", async (CancellationToken ct) =>
        {
            var s = await glossary.SummaryAsync(ct);
            return Results.Json(new { enabled = s.Enabled, entries = s.Entries, broken = s.Broken, notApplied = s.NotApplied });
        }).RequireAdmin(Permissions.DashboardView);

        app.MapGet("/api/admin/glossary/schema", async (CancellationToken ct) =>
        {
            var tables = await glossary.EntitiesAsync(ct);
            return Results.Json(tables.OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase).Select(t => new { entity = t.LogicalName, label = t.Label }));
        }).RequireAdmin(Permissions.GlossaryManage);

        app.MapGet("/api/admin/glossary/schema/{entity}", async (string entity, CancellationToken ct) =>
        {
            var info = await glossary.EntityAsync(entity, ct);
            if (info is null) { return Results.NotFound(); }
            return Results.Json(info.Attributes.OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new { attribute = a.LogicalName, label = a.Label, type = a.AttributeType, options = a.Options.Select(o => new { value = o.Value, label = o.Label }) }));
        }).RequireAdmin(Permissions.GlossaryManage);

        app.MapPost("/api/admin/glossary", async (GlossaryEntryDto dto, HttpContext http, CancellationToken ct) =>
            ToHttp(await glossary.SaveAsync(http.Admin().Account, ToEntry(dto), null, ct, CanSeeDocuments(http)))).RequireAdmin(Permissions.GlossaryManage);

        app.MapPut("/api/admin/glossary/{id}", async (string id, GlossaryEntryDto dto, HttpContext http, CancellationToken ct) =>
            ToHttp(await glossary.SaveAsync(http.Admin().Account, ToEntry(dto), id, ct, CanSeeDocuments(http)))).RequireAdmin(Permissions.GlossaryManage);

        app.MapPut("/api/admin/glossary/{id}/enabled", async (string id, GlossaryEnabledDto dto, HttpContext http, CancellationToken ct) =>
            ToHttp(await glossary.SetEnabledAsync(http.Admin().Account, id, dto.Enabled, ct))).RequireAdmin(Permissions.GlossaryManage);

        app.MapDelete("/api/admin/glossary/{id}", async (string id, HttpContext http, CancellationToken ct) =>
            ToHttp(await glossary.DeleteAsync(http.Admin().Account, id, ct))).RequireAdmin(Permissions.GlossaryManage);

        app.MapPost("/api/admin/glossary/try", async (GlossaryEntryDto dto, HttpContext http, CancellationToken ct) =>
        {
            var result = await glossary.TryAsync(ToEntry(dto), http.Caller(), ct);
            return Results.Json(new { outcome = result.Outcome, rows = result.Rows, capped = result.Capped, reason = result.Reason, text = result.Text });
        }).RequireAdmin(Permissions.GlossaryManage).RequireCaller();

        app.MapGet("/api/admin/glossary/export", async (HttpContext http, CancellationToken ct) =>
        {
            var json = await glossary.ExportAsync(http.Admin().Account, ct);
            http.Response.Headers.ContentDisposition = "attachment; filename=\"glossary.json\"";
            http.Response.Headers.CacheControl = "no-store";
            return Results.Text(json, "application/json");
        }).RequireAdmin(Permissions.GlossaryManage);

        app.MapPost("/api/admin/glossary/import", async (GlossaryImportDto dto, HttpContext http, CancellationToken ct) =>
        {
            var report = await glossary.ImportAsync(http.Admin().Account, dto.Json ?? "", dto.Apply == true, ct);
            if (report is null) { return Results.Json(new { code = "invalid_json", reason = "invalid_json" }, statusCode: StatusCodes.Status400BadRequest); }
            return Results.Json(new
            {
                total = report.Total,
                accepted = report.Accepted,
                applied = report.Applied,
                rejected = report.Rejected.Select(r => new { index = r.Index, term = r.Term, reason = r.Reason }),
            });
        }).RequireAdmin(Permissions.GlossaryManage);
    }

    private static bool CanSeeDocuments(HttpContext http) => http.Admin()!.Permissions.Contains(Permissions.DocumentsManage);

    private static GlossaryEntry ToEntry(GlossaryEntryDto dto) => new GlossaryEntry
    {
        Kind = dto.Kind ?? "",
        Terms = (dto.Terms ?? new List<GlossaryTermDto>()).Select(t => new GlossaryTerm(t.Text ?? "", t.Language ?? "any")).ToList(),
        Entity = dto.Entity ?? "",
        Attribute = dto.Attribute,
        Conditions = (dto.Conditions ?? new List<GlossaryConditionDto>()).Select(c => new GlossaryCondition(c.Attribute ?? "", c.Operator ?? "", c.Values ?? new List<string>())).ToList(),
        Description = dto.Description ?? "",
        Enabled = dto.Enabled ?? true,
    };

    private static IResult ToHttp(GlossaryAdminResult result)
    {
        if (result.Ok) { return Results.Json(new { id = result.EntryId, documents = result.Documents ?? Array.Empty<string>() }); }
        var status = result.Reason == "not_found" ? StatusCodes.Status404NotFound : result.Reason == "duplicate" ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity;
        return Results.Json(new { code = result.Reason, reason = result.Reason, term = result.Term }, statusCode: status);
    }
}
