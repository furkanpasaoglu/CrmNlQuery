using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Web.Contracts.Admin;

namespace CrmNlQuery.Web.Endpoints.Admin;

/// <summary>
/// Defines the admin panel language preference endpoints.
/// </summary>
public static class LanguageAdminEndpoints
{
    /// <summary>
    /// Registers the endpoint that saves or clears the administration panel language preference, rejecting unsupported languages with 422.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapPut("/api/admin/me/language", async (LanguageDto dto, HttpContext http, CancellationToken ct) =>
        {
            var principal = http.Admin();
            var set = await services.Preferences.SetLanguageAsync(principal!.Account, dto.Language, ct);
            if (!set.Ok)
            {
                return Results.Json(new { message = MessageCatalog.Now(Msg.LanguageUnsupported) }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            services.Language.Invalidate(principal.Account);
            var effective = await services.Language.ResolveAsync(principal.Account, http.Request.Headers.AcceptLanguage.ToString(), ct);
            return Results.Json(new { language = set.Saved, effectiveLanguage = effective });
        }).RequireAdmin(null);
    }
}
