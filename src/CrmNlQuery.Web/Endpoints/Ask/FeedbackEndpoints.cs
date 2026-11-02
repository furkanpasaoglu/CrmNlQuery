using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Feedback.Models;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Web.Endpoints.Ask;

/// <summary>
/// Defines the endpoints for a user to vote on their own result.
/// </summary>
public static class FeedbackEndpoints
{
    /// <summary>
    /// Registers the endpoints for rating a result with thumbs up or down, sharing it as a candidate, and withdrawing the vote.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapPut("/api/result/{id}/feedback", async (string id, FeedbackDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var result = await services.Feedback.RateAsync(callerId!.UserId, id, dto.Rating, dto.Reason, dto.Share == true, ct);
            return Respond(result);
        }).RequireCors("crm-widget").RequireCaller();

        app.MapDelete("/api/result/{id}/feedback", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            return Respond(await services.Feedback.RemoveAsync(callerId!.UserId, id, ct));
        }).RequireCors("crm-widget").RequireCaller();
    }

    private static IResult Respond(FeedbackResult result)
    {
        return result.Status switch
        {
            FeedbackStatus.Ok => Results.Json(new { feedback = result.State is null ? null : new { rating = result.State.Rating, reason = result.State.Reason, shared = result.State.Shared } }),
            FeedbackStatus.Invalid => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.NotFound(),
        };
    }
}
