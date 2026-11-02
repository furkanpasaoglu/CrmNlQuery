using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Admin;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Web.Endpoints.Admin.Data;

/// <summary>
/// Maps the admin endpoints for knowledge base documents written by business owners.
/// </summary>
public static class DocumentEndpoints
{
    /// <summary>
    /// Maps the document endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, KnowledgeService knowledge)
    {
        app.MapGet("/api/admin/documents", async (HttpContext http, CancellationToken ct) =>
        {
            var documents = (await knowledge.ListAsync(ct)).Select(d => new
            {
                id = d.Id,
                title = d.Title,
                enabled = d.Enabled,
                characters = d.Body.Length,
                updatedBy = d.UpdatedBy,
                updatedAt = d.UpdatedAt,
            });
            return Results.Json(new { searchEnabled = knowledge.Enabled, available = knowledge.Available, unavailableReason = knowledge.UnavailableReason, documents });
        }).RequireAdmin(Permissions.DocumentsManage);

        app.MapGet("/api/admin/documents/{id:guid}", async (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var d = await knowledge.GetAsync(id, ct);
            return d is null
                ? Results.Json(new { error = MessageCatalog.Now(Msg.AdmDocGone) }, statusCode: StatusCodes.Status404NotFound)
                : Results.Json(new { id = d.Id, title = d.Title, body = d.Body, enabled = d.Enabled, updatedBy = d.UpdatedBy, updatedAt = d.UpdatedAt });
        }).RequireAdmin(Permissions.DocumentsManage);

        app.MapPost("/api/admin/documents", async (DocumentDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await knowledge.CreateAsync(actor!, dto.Title, dto.Body, ct));
        }).RequireAdmin(Permissions.DocumentsManage);

        app.MapPut("/api/admin/documents/{id:guid}", async (Guid id, DocumentDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await knowledge.UpdateAsync(actor!, id, dto.Title, dto.Body, ct));
        }).RequireAdmin(Permissions.DocumentsManage);

        app.MapPut("/api/admin/documents/{id:guid}/enabled", async (Guid id, DocumentEnabledDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await knowledge.SetEnabledAsync(actor!, id, dto.Enabled, ct));
        }).RequireAdmin(Permissions.DocumentsManage);

        app.MapDelete("/api/admin/documents/{id:guid}", async (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await knowledge.DeleteAsync(actor!, id, ct));
        }).RequireAdmin(Permissions.DocumentsManage);

        app.MapPost("/api/admin/documents/search", async (SearchDto dto, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var hits = (await knowledge.SearchAsync(dto.Question ?? "", ct)).Select(s => new
                {
                    documentId = s.DocumentId,
                    title = s.Title,
                    text = s.Text,
                    score = Math.Round(s.Score, 4),
                    source = s.Source,
                });
                return Results.Json(hits);
            }
            catch (LlmUnavailableException ex)
            {
                return Results.Json(new { error = MessageCatalog.Now(Msg.AdmEmbedUnreachable, MessageCatalog.Args(("message", ex.Message))) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAdmin(Permissions.DocumentsManage);
    }
}
