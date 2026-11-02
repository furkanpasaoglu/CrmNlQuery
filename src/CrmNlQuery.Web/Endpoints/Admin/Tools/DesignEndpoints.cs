using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Design.Exceptions;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Web.Endpoints.Admin.Tools;

/// <summary>
/// Maps the HTTP endpoints for design.
/// </summary>
public static class DesignEndpoints
{
    private sealed record StartDto(string? Text, string? Prefix);

    /// <summary>
    /// Maps the design-assistant admin endpoints (start, list, get, download, cancel, delete) onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        object Summary(DesignJob j) => new
        {
            id = j.Id, title = j.Title, status = j.Status, step = j.Step, percent = j.Percent, problems = j.ProblemCount, error = j.Error,
            createdAt = j.CreatedAt, finishedAt = j.FinishedAt,
        };

        app.MapGet("/api/admin/design", async (HttpContext http, CancellationToken ct) =>
        {
            var jobs = await services.DesignJobs.ListAsync(http.Admin().Account, ct);
            return Results.Json(jobs.Select(Summary));
        }).RequireAdmin(Permissions.DesignUse);

        app.MapPost("/api/admin/design", async (StartDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            try
            {
                var job = await services.DesignJobs.StartAsync(actor.Account, dto.Text ?? "", dto.Prefix, ct);
                try
                {
                    await services.Access.RecordAsync(actor.Account, "design.start", job.Id, "customization design started", ct);
                }
                catch (Exception)
                {
                }
                return Results.Json(Summary(job), statusCode: StatusCodes.Status202Accepted);
            }
            catch (DesignJobException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.Code == "busy" ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest);
            }
        }).RequireAdmin(Permissions.DesignUse);

        app.MapGet("/api/admin/design/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var job = await services.DesignJobs.GetAsync(http.Admin().Account, id, ct);
            if (job is null)
            {
                return Results.NotFound();
            }
            var result = DesignJobService.ResultOf(job);
            return Results.Json(new
            {
                id = job.Id, title = job.Title, status = job.Status, step = job.Step, percent = job.Percent, problems = job.ProblemCount, error = job.Error,
                createdAt = job.CreatedAt, finishedAt = job.FinishedAt, request = job.Request, prefix = result?.Prefix ?? job.Prefix,
                markdown = result is null ? null : DesignRenderer.Markdown(result),
                tables = result?.Document.Tables.Count,
            });
        }).RequireAdmin(Permissions.DesignUse);

        app.MapGet("/api/admin/design/{id}/download", async (string id, string? format, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            var job = await services.DesignJobs.GetAsync(actor.Account, id, ct);
            var result = job is null ? null : DesignJobService.ResultOf(job);
            if (job is null || result is null)
            {
                return Results.NotFound();
            }
            var kind = (format ?? "md").ToLowerInvariant();
            var name = "design-" + job.Id.Substring(0, 8);
            byte[] bytes;
            string contentType;
            switch (kind)
            {
                case "md":
                    bytes = System.Text.Encoding.UTF8.GetBytes(DesignRenderer.Markdown(result));
                    contentType = "text/markdown; charset=utf-8";
                    name += ".md";
                    break;
                case "json":
                    bytes = System.Text.Encoding.UTF8.GetBytes(DesignRenderer.Json(result));
                    contentType = "application/json; charset=utf-8";
                    name += ".json";
                    break;
                case "docx":
                    bytes = DesignDocx.Build(result);
                    contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                    name += ".docx";
                    break;
                default:
                    return Results.Json(new { message = MessageCatalog.Now(Msg.DesignUnknownFormat) }, statusCode: StatusCodes.Status400BadRequest);
            }
            try
            {
                await services.Access.RecordAsync(actor.Account, "design.download", job.Id, "format=" + kind, ct);
            }
            catch (Exception)
            {
            }
            http.Response.Headers["Cache-Control"] = "no-store";
            return Results.File(bytes, contentType, name);
        }).RequireAdmin(Permissions.DesignUse);

        app.MapPost("/api/admin/design/{id}/cancel", async (string id, HttpContext http, CancellationToken ct) =>
            await services.DesignJobs.CancelAsync(http.Admin().Account, id, ct) ? Results.Accepted() : Results.NotFound())
            .RequireAdmin(Permissions.DesignUse);

        app.MapDelete("/api/admin/design/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                return await services.DesignJobs.DeleteAsync(http.Admin().Account, id, ct) ? Results.Ok() : Results.NotFound();
            }
            catch (DesignJobException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        }).RequireAdmin(Permissions.DesignUse);
    }
}
