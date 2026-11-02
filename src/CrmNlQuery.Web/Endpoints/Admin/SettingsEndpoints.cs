using CrmNlQuery.Application.Operations.Settings;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Admin;

namespace CrmNlQuery.Web.Endpoints.Admin;

/// <summary>
/// Maps the admin runtime settings endpoints for quotas, pausing, knowledge and other settings.
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// Maps the settings endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AccessService access, RuntimeSettings settings)
    {
        app.MapGet("/api/admin/settings", async (HttpContext http, CancellationToken ct) =>
        {
            var states = settings.GetStates();
            var groups = SettingsCatalog.Groups.Select(group => new
            {
                name = group,
                items = states.Where(st => st.Definition.Group == group && !SettingsCatalog.IsManagedByItsOwnEndpoint(st.Definition.Key)).Select(st => new
                {
                    key = st.Definition.Key,
                    description = st.Definition.Description,
                    kind = st.Definition.Kind.ToString().ToLowerInvariant(),
                    min = st.Definition.Min,
                    max = st.Definition.Max,
                    choices = st.Definition.Choices,
                    value = st.Effective,
                    source = st.Source,
                    @default = st.Default,
                }),
            });
            return Results.Json(new { askEnabled = settings.Limits.AskEnabled, groups });
        }).RequireAdmin(Permissions.SettingsManage);

        app.MapPut("/api/admin/settings", async (string key, SettingValueDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            if (SettingsCatalog.IsManagedByItsOwnEndpoint(key))
            {
                return Results.NotFound();
            }
            if (SettingsCatalog.RequiredPermission(key) is { } needed && !actor!.Has(needed))
            {
                return Results.NotFound();
            }
            var result = await settings.SetAsync(actor!.Account, key, dto.Value, ct);
            await RecordSwitchAsync(app, access, actor.Account, key, dto.Value, result.Ok, ct);
            return AdminEndpoints.ToHttp(result);
        }).RequireAdmin(Permissions.SettingsManage);

        app.MapDelete("/api/admin/settings", async (string key, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            if (SettingsCatalog.IsManagedByItsOwnEndpoint(key))
            {
                return Results.NotFound();
            }
            if (SettingsCatalog.RequiredPermission(key) is { } needed && !actor!.Has(needed))
            {
                return Results.NotFound();
            }
            var result = await settings.ResetAsync(actor!.Account, key, ct);
            await RecordSwitchAsync(app, access, actor.Account, key, "reset to default", result.Ok, ct);
            return AdminEndpoints.ToHttp(result);
        }).RequireAdmin(Permissions.SettingsManage);
    }

    private static async Task RecordSwitchAsync(IEndpointRouteBuilder app, AccessService access, string actor, string key, string? value, bool ok, CancellationToken ct)
    {
        if (!ok || key != SettingsCatalog.DataPolicy)
        {
            return;
        }
        try
        {
            await access.RecordAsync(actor, "datapolicy.switch", key, value ?? "", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminEndpoints.AuditLogger(app).LogWarning(ex, "Could not write the datapolicy.switch audit record.");
        }
    }
}
