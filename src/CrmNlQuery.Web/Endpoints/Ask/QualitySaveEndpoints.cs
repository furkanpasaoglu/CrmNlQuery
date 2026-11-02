using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Ask;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Web.Endpoints.Ask;

/// <summary>
/// Widget endpoint where a reviewer adds their own answer to the acceptance set; the permission is checked on every call.
/// </summary>
public static class QualitySaveEndpoints
{
    /// <summary>
    /// Registers the endpoint that saves a result as an acceptance-set case, requiring the quality permission and returning 404 when the feature is off.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapPost("/api/result/{id}/quality/save", async (string id, QualitySaveDto dto, HttpContext http, CancellationToken ct) =>
        {
            var caller = http.Caller();
            if (!services.Quality.Enabled || !await WidgetPermissions.HasAsync(http, services, Permissions.QualityManage, ct))
            {
                return Results.NotFound();
            }
            try
            {
                var rule = new QualitySaveRule(dto.Order ?? "", dto.Columns ?? "");
                var result = await services.Quality.SaveFromResultAsync(caller.UserId, WidgetPermissions.Account(http, services)!, id, rule, dto.Confirmed == true, caller, RequestLanguage.Current, ct);
                return result.Status switch
                {
                    QualitySaveStatus.Saved => Results.Json(new { saved = true, rows = result.Rows }),
                    QualitySaveStatus.Duplicate => Results.Json(new { saved = false, duplicate = true, rows = result.Rows }),
                    QualitySaveStatus.NotFound => Results.NotFound(),
                    QualitySaveStatus.DataClosed => Results.Json(new { error = MessageCatalog.Now(Msg.QualityClosed), reason = "closed" }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.Json(new { error = Message(result), reason = result.Reason ?? result.Status.ToString().ToLowerInvariant() }, statusCode: StatusCodes.Status422UnprocessableEntity),
                };
            }
            catch (CrmException ex) when (ex.IsPermissionDenied)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (CrmException ex)
            {
                return Results.Json(new { error = UserMessages.ForCrm(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/result/{id}/quality/suggest", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var caller = http.Caller();
            if (!services.Quality.SuggestionsAllowed)
            {
                return Results.NotFound();
            }
            try
            {
                var result = await services.Quality.SuggestFromResultAsync(caller.UserId, id, caller, RequestLanguage.Current, ct);
                return result.Status switch
                {
                    QualitySaveStatus.Saved => Results.Json(new { saved = true }),
                    QualitySaveStatus.Duplicate => Results.Json(new { saved = false, duplicate = true }),
                    QualitySaveStatus.NotFound or QualitySaveStatus.Disabled => Results.NotFound(),
                    QualitySaveStatus.DataClosed => Results.Json(new { error = MessageCatalog.Now(Msg.QualityClosed), reason = "closed" }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.Json(new { error = Message(result), reason = result.Reason ?? result.Status.ToString().ToLowerInvariant() }, statusCode: StatusCodes.Status422UnprocessableEntity),
                };
            }
            catch (CrmException ex) when (ex.IsPermissionDenied)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (CrmException ex)
            {
                return Results.Json(new { error = UserMessages.ForCrm(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireCors("crm-widget").RequireCaller();
    }

    private static string Message(QualitySaveResult result) => MessageCatalog.Now(result switch
    {
        { Status: QualitySaveStatus.TooManySuggestions } => Msg.QualitySuggestTooMany,
        { Status: QualitySaveStatus.TooWeak, Reason: "truncated" } => Msg.QualityTruncated,
        { Status: QualitySaveStatus.TooWeak } => Msg.QualityEmpty,
        { Status: QualitySaveStatus.NotSupported, Reason: "derived" } => Msg.QualityDerived,
        { Status: QualitySaveStatus.NotSupported } => Msg.QualityChain,
        { Status: QualitySaveStatus.LimitReached } => Msg.QualityLimit,
        _ => Msg.QualityInvalid,
    });
}
