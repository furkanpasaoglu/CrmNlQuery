using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Export.Exceptions;
using CrmNlQuery.Application.Export.Models;
using CrmNlQuery.Application.Export.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Web.Endpoints.Admin.Tools;

/// <summary>
/// Defines the endpoints that download records as CSV or JSON Lines files.
/// </summary>
public static class ExportEndpoints
{
    /// <summary>
    /// Registers the admin export endpoints: the column schema and the streaming per-dataset download, including the check-only mode.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapGet("/api/admin/export/schema", () =>
        {
            return Results.Json(ExportDatasets.All.Where(d => d != ExportDatasets.Questions || services.QuestionViews.Enabled).Select(dataset => new
            {
                dataset,
                permission = ExportDatasets.ViewPermission(dataset),
                columns = ExportSchema.For(dataset).Select(c => new { name = c.Name, description = c.Description, type = c.IsText ? "text" : "number" }),
            }));
        }).RequireAdmin(Permissions.ExportRun);

        app.MapGet("/api/admin/export/{dataset}", async (string dataset, HttpContext http, string? format, DateTimeOffset? from, DateTimeOffset? to,
            string? account, Guid? user, string? table, string? kind, string? tz, bool? check, CancellationToken ct) =>
        {
            var actor = http.Admin();
            var end = to ?? DateTimeOffset.UtcNow;
            var request = new ExportRequest(dataset.ToLowerInvariant(),
                string.Equals(format, "jsonl", StringComparison.OrdinalIgnoreCase) ? ExportFormat.JsonLines : ExportFormat.Csv,
                from ?? end.AddDays(-30), end)
            {
                Account = account,
                UserId = user,
                Table = table,
                Kind = kind,
                TimeZone = tz,
            };

            ExportPlan plan;
            try
            {
                plan = services.Export.Prepare(actor, request);
            }
            catch (ExportException ex)
            {
                var status = ex.Code switch
                {
                    "forbidden" => StatusCodes.Status403Forbidden,
                    "busy" => StatusCodes.Status429TooManyRequests,
                    "unavailable" => StatusCodes.Status503ServiceUnavailable,
                    _ => StatusCodes.Status400BadRequest,
                };
                return Results.Json(new { message = ex.Message }, statusCode: status);
            }

            if (check == true)
            {
                plan.Dispose();
                return Results.Json(new { ok = true, fileName = plan.FileName });
            }

            using (plan)
            {
                http.Response.ContentType = plan.ContentType;
                http.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{plan.FileName}\"";
                http.Response.Headers["Cache-Control"] = "no-store";
                http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
                try
                {
                    await services.Export.StreamAsync(actor, plan, http.Response.Body, ct);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is IOException)
                {
                }
                catch (Exception) when (!http.Response.HasStarted)
                {
                    http.Response.Headers.Remove("Content-Disposition");
                    return Results.Json(new { message = MessageCatalog.Now(Msg.ExportCouldNotStart) }, statusCode: StatusCodes.Status500InternalServerError);
                }
            }
            return Results.Empty;
        }).RequireAdmin(Permissions.ExportRun);
    }
}
