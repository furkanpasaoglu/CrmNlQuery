using System.Text.Json;
using CrmNlQuery.Composition;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;

namespace CrmNlQuery.Web.Endpoints.Ask;

/// <summary>
/// Maps the endpoints for the caller's own user preferences, with partial PUT writes and CORS for the widget.
/// </summary>
public static class PreferencesEndpoints
{
    /// <summary>
    /// Registers the preferences endpoints on the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapGet("/api/me/preferences", async (HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var name = await services.Preferences.GetDisplayNameAsync(callerId!.UserId, ct);
            return Results.Json(await Shape(http, services, name, ct));
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPut("/api/me/preferences", async (JsonElement body, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var account = services.Identity.GetAccount(WindowsAccount.From(http.User));
            string? name = await services.Preferences.GetDisplayNameAsync(callerId!.UserId, ct);
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("language", out var languageProperty))
            {
                var text = languageProperty.ValueKind == JsonValueKind.String ? languageProperty.GetString() : null;
                if (account is null)
                {
                    return Results.Json(new { message = MessageCatalog.Now(Msg.LanguageCannotSave) }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
                var set = await services.Preferences.SetLanguageAsync(account, text, ct);
                if (!set.Ok)
                {
                    return Results.Json(new { message = MessageCatalog.Now(Msg.LanguageUnsupported) }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }
                services.Language.Invalidate(account);
                RequestLanguage.Set(await services.Language.ResolveAsync(account, http.Request.Headers.AcceptLanguage.ToString(), ct));
            }
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("displayName", out var nameProperty))
            {
                name = await services.Preferences.SetDisplayNameAsync(callerId.UserId, nameProperty.ValueKind == JsonValueKind.String ? nameProperty.GetString() : null, ct);
            }
            return Results.Json(await Shape(http, services, name, ct));
        }).RequireCors("crm-widget").RequireCaller();
    }

    private static async Task<object> Shape(HttpContext http, AppServices services, string? name, CancellationToken ct)
    {
        var account = services.Identity.GetAccount(WindowsAccount.From(http.User));
        var saved = account is null ? null : await services.Preferences.GetLanguageAsync(account, ct);
        return new
        {
            displayName = name,
            language = saved,
            effectiveLanguage = RequestLanguage.Current,
            languages = CrmNlQuery.Domain.Common.Constants.UiLanguage.All,
        };
    }
}
