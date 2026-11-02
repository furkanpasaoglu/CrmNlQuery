using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Web.Contracts.Ask;
using CrmNlQuery.Application.Operations.Settings;

namespace CrmNlQuery.Web.Endpoints.Ask;

/// <summary>
/// HTTP endpoints for chat sessions, limited to the caller's own chats.
/// </summary>
public static class ChatEndpoints
{
    /// <summary>
    /// Registers the chat session endpoints: paged chat list with retention note, messages, rename, pin, delete and counts, all scoped to the calling user.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapGet("/api/chats", async (HttpContext http, int? take, string? cursor, string? q, bool? pinned, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var list = await services.Chats.ListAsync(callerId!.UserId, take, cursor, q, pinned, ct);
            return Results.Json(new
            {
                items = list.Items,
                nextCursor = list.NextCursor,
                retentionDays = services.Settings.GetInt(SettingsCatalog.HistoryRetentionDays),
                language = CrmNlQuery.Application.Localization.Services.RequestLanguage.Current,
                languages = CrmNlQuery.Domain.Common.Constants.UiLanguage.All,
                auditNotice = services.QuestionViews.Enabled,
                feedback = new { enabled = services.Feedback.Enabled, allowSharing = services.Feedback.AllowSharing },
                quality = new { canSave = services.Quality.Enabled && await WidgetPermissions.HasAsync(http, services, CrmNlQuery.Domain.Access.Constants.Permissions.QualityManage, ct), canSuggest = services.Quality.SuggestionsAllowed },
                exampleQuestions = CrmNlQuery.Application.Ask.Services.ExampleQuestions.For(CrmNlQuery.Application.Localization.Services.RequestLanguage.Current, services.ExampleQuestions, services.ExampleQuestionsTurkish),
            });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapGet("/api/chats/counts", async (HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var counts = await services.Chats.CountsAsync(callerId!.UserId, ct);
            return Results.Json(new { total = counts.Total, pinned = counts.Pinned });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapGet("/api/chats/{id}/messages", async (string id, HttpContext http, int? take, string? cursor, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var messages = await services.Chats.MessagesAsync(callerId!.UserId, id, take, cursor, ct);
            if (messages is null)
            {
                return Results.NotFound();
            }
            var states = await services.Feedback.StatesAsync(callerId.UserId, messages.Items.Select(m => m.Id).ToList(), ct);
            return Results.Json(new
            {
                items = messages.Items.Select(m => new { id = m.Id, question = m.Question, entity = m.Entity, createdAt = m.CreatedAt, feedback = states.TryGetValue(m.Id, out var s) ? new { rating = s.Rating, reason = s.Reason, shared = s.Shared } : null }),
                nextCursor = messages.NextCursor,
            });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapMethods("/api/chats/{id}", new[] { "PATCH" }, async (string id, ChatPatchDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            if (dto.Title is null && dto.Pinned is null)
            {
                return Results.BadRequest();
            }
            if (dto.Title is not null && ChatService.CleanTitle(dto.Title).Length == 0)
            {
                return Results.BadRequest();
            }
            if (await services.Chats.OwnsAsync(callerId!.UserId, id, ct) is false)
            {
                return Results.NotFound();
            }
            if (dto.Title is not null)
            {
                await services.Chats.RenameAsync(callerId.UserId, id, dto.Title, ct);
            }
            if (dto.Pinned is not null)
            {
                await services.Chats.PinAsync(callerId.UserId, id, dto.Pinned.Value, ct);
            }
            return Results.Ok();
        }).RequireCors("crm-widget").RequireCaller();

        app.MapDelete("/api/chats/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            return await services.Chats.DeleteAsync(callerId!.UserId, id, ct) ? Results.Ok() : Results.NotFound();
        }).RequireCors("crm-widget").RequireCaller();

        app.MapDelete("/api/chats", async (HttpContext http, bool? includePinned, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var deleted = await services.Chats.DeleteAllAsync(callerId!.UserId, includePinned ?? false, ct);
            await services.OpenInCrm.DeleteViewAsync(callerId.UserId, ct);
            return Results.Json(new { deleted });
        }).RequireCors("crm-widget").RequireCaller();
    }
}
