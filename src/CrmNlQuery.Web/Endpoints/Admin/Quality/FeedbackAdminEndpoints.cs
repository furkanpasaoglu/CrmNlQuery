using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Web.Endpoints.Admin.Quality;

/// <summary>
/// Maps the feedback admin endpoints: general statistics and the candidates users chose to share.
/// </summary>
public static class FeedbackAdminEndpoints
{
    /// <summary>
    /// Maps the feedback admin endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapGet("/api/admin/feedback/summary", async (int? days, CancellationToken ct) =>
        {
            var s = await services.Feedback.SummaryAsync(days ?? 7, ct);
            return Results.Json(new
            {
                enabled = services.Feedback.Enabled,
                up = s.Up,
                down = s.Down,
                reasons = s.Reasons.Select(r => new { reason = r.Key, count = r.Count }),
                downTables = s.DownTables.Select(t => new { table = t.Key, count = t.Count }),
                downSingleStep = s.DownSingleStep,
                downMultiStep = s.DownMultiStep,
                days = s.Days.Select(d => new { day = d.Day.ToString("yyyy-MM-dd"), up = d.Up, down = d.Down }),
            });
        }).RequireAdmin(Permissions.DashboardView);

        app.MapGet("/api/admin/suggestions/summary", async (int? days, CancellationToken ct) =>
        {
            var window = Math.Clamp(days ?? 7, 1, 365);
            var stats = await services.SuggestionStats.SummaryAsync(window, ct);
            return Results.Json(new
            {
                days = window,
                kinds = stats.Select(s => new { kind = s.Kind, shown = s.Shown, clicked = s.Clicked }),
            });
        }).RequireAdmin(Permissions.DashboardView);

        app.MapGet("/api/admin/feedback/candidates", async (HttpContext http, int? take, CancellationToken ct) =>
        {
            var actor = http.Admin();
            var list = await services.Feedback.CandidatesAsync(actor.Account, take ?? 50, ct);
            return Results.Json(new
            {
                retentionDays = services.Settings.GetInt(CrmNlQuery.Application.Operations.Settings.SettingsCatalog.FeedbackRetentionDays),
                candidates = list.Select(c => new { id = c.ResultId, question = c.Question, explanation = c.Explanation, reason = c.Reason, createdAt = c.CreatedAt }),
            });
        }).RequireAdmin(Permissions.FeedbackView);

        app.MapDelete("/api/admin/feedback/candidates/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return await services.Feedback.DeleteCandidateAsync(actor.Account, id, ct) ? Results.Ok() : Results.NotFound();
        }).RequireAdmin(Permissions.FeedbackView);
    }
}
