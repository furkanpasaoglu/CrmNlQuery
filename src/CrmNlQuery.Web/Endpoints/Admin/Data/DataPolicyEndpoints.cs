using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.DataPolicy.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Admin.Data;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Web.Endpoints.Admin.Data;

/// <summary>
/// Maps the Data access admin endpoints for deciding which CRM tables and fields the chat tool may use.
/// </summary>
public static class DataPolicyEndpoints
{
    /// <summary>
    /// Maps the data policy endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        var admin = services.DataPolicyAdmin;

        app.MapGet("/api/admin/data-policy/tables", async (string? q, CancellationToken ct) =>
        {
            return await Safe(async () =>
            {
                var snapshot = await services.DataPolicy.GetAsync(ct);
                var tables = await admin.ListTablesAsync(q, ct);
                return Results.Json(new
                {
                    enabled = snapshot.Enabled,
                    persistent = services.DataPolicy.Persistent,
                    decisions = snapshot.Rules.Count,
                    defaultTables = snapshot.DefaultTables.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    tables = tables.Select(t => new { entity = t.Entity, label = t.Label, open = t.Open, source = t.Source, autoClosedFields = t.AutoClosedFields, adminClosedFields = t.AdminClosedFields }),
                });
            });
        }).RequireAdmin(Permissions.DataPolicyManage);

        app.MapGet("/api/admin/data-policy/review", async (CancellationToken ct) =>
        {
            return await Safe(async () =>
            {
                var rows = await admin.ReviewAsync(ct);
                return Results.Json(rows.Select(r => new { entity = r.Entity, entityLabel = r.EntityLabel, attribute = r.Attribute, label = r.Label }));
            });
        }).RequireAdmin(Permissions.DataPolicyManage);

        app.MapGet("/api/admin/data-policy/tables/{entity}/fields", async (string entity, CancellationToken ct) =>
        {
            return await Safe(async () =>
            {
                var rows = await admin.ListFieldsAsync(entity, ct);
                return rows is null
                    ? Results.NotFound()
                    : Results.Json(rows.Select(f => new { attribute = f.Attribute, label = f.Label, type = f.Type, open = f.Open, state = f.State, locked = f.Locked }));
            });
        }).RequireAdmin(Permissions.DataPolicyManage);

        app.MapPut("/api/admin/data-policy/tables/{entity}", async (string entity, DataPolicySetDto dto, HttpContext http, CancellationToken ct) =>
        {
            return await Safe(async () => ToHttp(await admin.SetTableAsync(http.Admin().Account, entity, dto.Allowed, http.Caller(), ct)));
        }).RequireAdmin(Permissions.DataPolicyManage).RequireCaller();

        app.MapPut("/api/admin/data-policy/tables/{entity}/fields/{attribute}", async (string entity, string attribute, DataPolicySetDto dto, HttpContext http, CancellationToken ct) =>
        {
            return await Safe(async () => ToHttp(await admin.SetFieldAsync(http.Admin().Account, entity, attribute, dto.Allowed, ct)));
        }).RequireAdmin(Permissions.DataPolicyManage);

        app.MapPost("/api/admin/data-policy/tables/{entity}/check", async (string entity, HttpContext http, CancellationToken ct) =>
        {
            var result = await admin.CheckAccessAsync(entity, http.Caller(), ct);
            return Results.Json(new { outcome = result.Outcome, message = result.Message });
        }).RequireAdmin(Permissions.DataPolicyManage).RequireCaller();
    }

    private static IResult ToHttp(DataPolicyResult result)
    {
        return result.Ok ? Results.Ok() : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> Safe(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CrmException ex)
        {
            return Results.Json(new { error = UserMessages.ForCrm(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
