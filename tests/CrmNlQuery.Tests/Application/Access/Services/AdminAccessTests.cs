using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests the admin access gate: permission checks, not-found responses and fail-closed behavior.
/// </summary>
public class AdminAccessTests
{
    private const string Boss = @"LAB\boss";

    private static async Task<(AdminAccess Access, AccessService Service, AccessPrincipal Admin)> Setup()
    {
        var service = new AccessService(new MemoryAccessStore(), new[] { Boss });
        await service.EnsureBootstrapAsync();
        var admin = (await service.ResolveAsync(Boss))!;
        return (new AdminAccess(service), service, admin);
    }

    /// <summary>
    /// Verifies that a registered user with the permission is allowed and gets the principal.
    /// </summary>
    [Fact]
    public async Task ARegisteredUser_WithThePermission_IsAllowed_AndGetsThePrincipal()
    {
        var (access, _, _) = await Setup();

        var result = await access.AuthorizeAsync(Boss, Permissions.UsersManage);

        Assert.Equal(AdminDecision.Allowed, result.Decision);
        Assert.Equal(@"lab\boss", result.Principal!.Account);
    }

    /// <summary>
    /// Verifies that without a permission requirement any registered active user is allowed.
    /// </summary>
    [Fact]
    public async Task WithoutAPermissionRequirement_AnyRegisteredActiveUserIsAllowed()
    {
        var (access, service, admin) = await Setup();
        await service.AddUserAsync(admin, @"LAB\plain", null, new Guid[0]);

        Assert.Equal(AdminDecision.Allowed, (await access.AuthorizeAsync(@"LAB\plain", null)).Decision);
    }

    /// <summary>
    /// Verifies that a registered user without the permission gets not found so the panel does not reveal itself.
    /// </summary>
    [Fact]
    public async Task ARegisteredUser_WithoutThePermission_GetsNotFound_SoThePanelDoesNotRevealItself()
    {
        var (access, service, admin) = await Setup();
        var roleId = (await service.CreateRoleAsync(admin, "Logs", new[] { Permissions.LogsView })).Id!.Value;
        await service.AddUserAsync(admin, @"LAB\logger", null, new[] { roleId });

        var result = await access.AuthorizeAsync(@"LAB\logger", Permissions.UsersManage);

        Assert.Equal(AdminDecision.NotFound, result.Decision);
        Assert.Null(result.Principal);
        Assert.Equal(AdminAccess.MissingPermission, result.Reason);
    }

    /// <summary>
    /// Verifies that an unregistered or inactive account is denied with the no access reason.
    /// </summary>
    [Fact]
    public async Task AnUnregisteredOrInactiveAccount_IsDeniedWithTheNoAccessReason()
    {
        var (access, service, admin) = await Setup();
        await service.AddUserAsync(admin, @"LABgone", null, new Guid[0]);
        await service.SetUserActiveAsync(admin, @"LABgone", false);

        Assert.Equal(AdminAccess.NoAccess, (await access.AuthorizeAsync(@"LABstranger", null)).Reason);
        Assert.Equal(AdminAccess.NoAccess, (await access.AuthorizeAsync(@"LABgone", null)).Reason);
        Assert.Null((await access.AuthorizeAsync(Boss, null)).Reason);
    }

    /// <summary>
    /// Verifies that several comma-separated permissions mean any of them.
    /// </summary>
    [Fact]
    public async Task SeveralPermissionsSeparatedByCommas_MeanAnyOfThem()
    {
        var (access, service, admin) = await Setup();
        var roleId = (await service.CreateRoleAsync(admin, "Users", new[] { Permissions.UsersManage })).Id!.Value;
        await service.AddUserAsync(admin, @"LAB\um", null, new[] { roleId });

        var either = await access.AuthorizeAsync(@"LAB\um", Permissions.UsersManage + "," + Permissions.RolesManage);
        var neither = await access.AuthorizeAsync(@"LAB\um", Permissions.AuditView + "," + Permissions.RolesManage);

        Assert.Equal(AdminDecision.Allowed, either.Decision);
        Assert.Equal(AdminDecision.NotFound, neither.Decision);
    }

    /// <summary>
    /// Verifies that an unregistered or empty account gets not found.
    /// </summary>
    [Theory]
    [InlineData(@"LAB\stranger")]
    [InlineData("")]
    public async Task AnUnregisteredAccount_GetsNotFound(string account)
    {
        var (access, _, _) = await Setup();

        Assert.Equal(AdminDecision.NotFound, (await access.AuthorizeAsync(account, null)).Decision);
    }

    /// <summary>
    /// Verifies that an inactive user gets not found.
    /// </summary>
    [Fact]
    public async Task AnInactiveUser_GetsNotFound()
    {
        var (access, service, admin) = await Setup();
        await service.AddUserAsync(admin, @"LAB\gone", null, new Guid[0]);
        await service.SetUserActiveAsync(admin, @"LAB\gone", false);

        Assert.Equal(AdminDecision.NotFound, (await access.AuthorizeAsync(@"LAB\gone", null)).Decision);
    }

    /// <summary>
    /// Verifies that when the store fails the answer is unavailable and never allowed.
    /// </summary>
    [Fact]
    public async Task WhenTheStoreFails_TheAnswerIsUnavailable_NeverAllowed()
    {
        var service = new AccessService(new FailingStore(), new string[0]);
        var access = new AdminAccess(service);

        var result = await access.AuthorizeAsync(Boss, Permissions.DashboardView);

        Assert.Equal(AdminDecision.Unavailable, result.Decision);
        Assert.Null(result.Principal);
    }

    private sealed class FailingStore : IAccessStore
    {
        private static Exception Boom() => new InvalidOperationException("store down");
        /// <summary>
        /// Test fake that returns the list of users.
        /// </summary>
        public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that returns a user by account.
        /// </summary>
        public Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that saves a user.
        /// </summary>
        public Task SaveUserAsync(AccessUser user, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that deletes a user by account.
        /// </summary>
        public Task DeleteUserAsync(string account, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that returns the list of roles.
        /// </summary>
        public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that returns a role by id.
        /// </summary>
        public Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that saves a role.
        /// </summary>
        public Task SaveRoleAsync(AccessRole role, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that deletes a role by id.
        /// </summary>
        public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that appends an audit entry.
        /// </summary>
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that returns audit entries up to the limit.
        /// </summary>
        public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that checks whether an account has audit entries as actor.
        /// </summary>
        public Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that returns the group mappings.
        /// </summary>
        public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that saves a group mapping.
        /// </summary>
        public Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Test fake that deletes a group mapping.
        /// </summary>
        public Task DeleteGroupMappingAsync(string group, CancellationToken ct = default) => throw Boom();
    }
}
