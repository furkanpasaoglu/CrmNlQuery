using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Admin;

namespace CrmNlQuery.Web.Endpoints.Admin;

/// <summary>
/// Maps the HTTP endpoints for access admin.
/// </summary>
public static class AccessAdminEndpoints
{
    /// <summary>
    /// Maps the access-administration endpoints for users, roles, groups and audit onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AccessService access)
    {
        app.MapGet("/api/admin/users", async (HttpContext http, CancellationToken ct) =>
        {
            var roles = (await access.ListRolesAsync(ct)).ToDictionary(r => r.Id, r => r.Name);
            var users = (await access.ListUsersAsync(ct)).Select(u => new
            {
                account = u.Account,
                displayName = u.DisplayName,
                active = u.Active,
                createdAt = u.CreatedAt,
                createdBy = u.CreatedBy,
                roleIds = u.RoleIds,
                roles = u.RoleIds.Where(roles.ContainsKey).Select(id => roles[id]).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            });
            return Results.Json(users);
        }).RequireAdmin(Permissions.UsersManage);

        app.MapPost("/api/admin/users", async (NewUserDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.AddUserAsync(actor!, dto.Account ?? "", dto.DisplayName, dto.RoleIds ?? new List<Guid>(), ct));
        }).RequireAdmin(Permissions.UsersManage);

        app.MapPut("/api/admin/users/roles", async (string account, UserRolesDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.SetUserRolesAsync(actor!, account, dto.RoleIds ?? new List<Guid>(), ct));
        }).RequireAdmin(Permissions.UsersManage);

        app.MapPut("/api/admin/users/active", async (string account, UserActiveDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.SetUserActiveAsync(actor!, account, dto.Active, ct));
        }).RequireAdmin(Permissions.UsersManage);

        app.MapDelete("/api/admin/users", async (string account, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.DeleteUserAsync(actor!, account, ct));
        }).RequireAdmin(Permissions.UsersManage);

        app.MapGet("/api/admin/groups", async (HttpContext http, CancellationToken ct) =>
        {
            var roles = (await access.ListRolesAsync(ct)).ToDictionary(r => r.Id, r => r.Name);
            var groups = (await access.ListGroupMappingsAsync(ct)).Select(g => new
            {
                group = g.DisplayName,
                roleIds = g.RoleIds,
                roles = g.RoleIds.Where(roles.ContainsKey).Select(id => roles[id]).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            });
            return Results.Json(groups);
        }).RequireAdmin(Permissions.UsersManage);

        app.MapPut("/api/admin/groups/roles", async (string group, UserRolesDto dto, bool? confirm, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.SetGroupRolesAsync(actor!, group, dto.RoleIds ?? new List<Guid>(), ct, confirm ?? false));
        }).RequireAdmin(Permissions.UsersManage);

        app.MapDelete("/api/admin/groups", async (string group, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.SetGroupRolesAsync(actor!, group, new List<Guid>(), ct));
        }).RequireAdmin(Permissions.UsersManage);

        app.MapGet("/api/admin/roles", async (HttpContext http, CancellationToken ct) =>
        {
            var roles = (await access.ListRolesAsync(ct)).Select(r => new
            {
                id = r.Id,
                name = r.Name,
                builtin = r.Builtin,
                permissions = r.Builtin ? Permissions.All.Select(p => p.Key).ToList() : r.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            });
            return Results.Json(roles);
        }).RequireAdmin(Permissions.UsersManage + "," + Permissions.RolesManage);

        app.MapPost("/api/admin/roles", async (RoleDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.CreateRoleAsync(actor!, dto.Name ?? "", dto.Permissions ?? new List<string>(), ct));
        }).RequireAdmin(Permissions.RolesManage);

        app.MapPut("/api/admin/roles/{id:guid}", async (Guid id, RoleDto dto, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.UpdateRoleAsync(actor!, id, dto.Name ?? "", dto.Permissions ?? new List<string>(), ct));
        }).RequireAdmin(Permissions.RolesManage);

        app.MapDelete("/api/admin/roles/{id:guid}", async (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var actor = http.Admin();
            return AdminEndpoints.ToHttp(await access.DeleteRoleAsync(actor!, id, ct));
        }).RequireAdmin(Permissions.RolesManage);
    }
}
