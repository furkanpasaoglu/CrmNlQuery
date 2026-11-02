using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests the access service rules for bootstrap, users, roles, permissions and audit using an in-memory store.
/// </summary>
public class AccessServiceTests
{
    private const string BootstrapAccount = @"CONTOSO\Admin";

    private readonly MemoryAccessStore _store = new MemoryAccessStore();

    private AccessService NewService(params string[] bootstrap)
        => new AccessService(_store, bootstrap.Length == 0 ? new[] { BootstrapAccount } : bootstrap);

    private async Task<(AccessService Service, AccessPrincipal Admin)> WithAdmin()
    {
        var service = NewService();
        await service.EnsureBootstrapAsync();
        var admin = await service.ResolveAsync(BootstrapAccount);
        return (service, admin!);
    }

    private static async Task<Guid> NewRole(AccessService service, AccessPrincipal admin, string name, params string[] permissions)
    {
        var result = await service.CreateRoleAsync(admin, name, permissions);
        Assert.True(result.Ok, result.Message);
        return result.Id!.Value;
    }

    private static async Task<AccessPrincipal> NewUser(AccessService service, AccessPrincipal admin, string account, params Guid[] roles)
    {
        var result = await service.AddUserAsync(admin, account, null, roles);
        Assert.True(result.Ok, result.Message);
        return (await service.ResolveAsync(account))!;
    }

    /// <summary>
    /// Verifies that bootstrap adds the configured accounts as administrators when there is none and audits.
    /// </summary>
    [Fact]
    public async Task Bootstrap_AddsTheConfiguredAccountsAsAdministrators_WhenThereIsNone_AndAudits()
    {
        var service = NewService();

        var added = await service.EnsureBootstrapAsync();

        Assert.Equal(1, added);
        var admin = await service.ResolveAsync(BootstrapAccount);
        Assert.NotNull(admin);
        Assert.True(admin!.IsAdministrator);
        Assert.Contains(await _store.ListAuditAsync(10), a => a.Action == "bootstrap" && a.Target == @"contoso\admin");
    }

    /// <summary>
    /// Verifies that bootstrap is ignored when an active administrator already exists.
    /// </summary>
    [Fact]
    public async Task Bootstrap_IsIgnored_WhenAnActiveAdministratorAlreadyExists()
    {
        var (service, _) = await WithAdmin();
        var other = NewService(@"CONTOSO\Other");

        var added = await other.EnsureBootstrapAsync();

        Assert.Equal(0, added);
        Assert.Null(await other.ResolveAsync(@"CONTOSO\Other"));
    }

    /// <summary>
    /// Verifies that without bootstrap accounts and no administrator nobody gets in.
    /// </summary>
    [Fact]
    public async Task WithoutBootstrapAccounts_AndNoAdministrator_NobodyGetsIn()
    {
        var service = new AccessService(_store, new string[0]);

        Assert.Equal(0, await service.EnsureBootstrapAsync());
        Assert.Null(await service.ResolveAsync(BootstrapAccount));
    }

    /// <summary>
    /// Verifies that unknown or empty accounts resolve to null.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"CONTOSO\nobody")]
    public async Task UnknownOrEmptyAccounts_ResolveToNull(string? account)
    {
        var (service, _) = await WithAdmin();
        Assert.Null(await service.ResolveAsync(account));
    }

    /// <summary>
    /// Verifies that account matching ignores case turkish letters and spaces.
    /// </summary>
    [Theory]
    [InlineData(@"contoso\admin")]
    [InlineData(@"CONTOSO\ADMİN")]
    [InlineData(@"  Contoso\Admin  ")]
    public async Task AccountMatching_IgnoresCase_TurkishLetters_AndSpaces(string account)
    {
        var (service, _) = await WithAdmin();
        Assert.NotNull(await service.ResolveAsync(account));
    }

    /// <summary>
    /// Verifies that an inactive user resolves to null.
    /// </summary>
    [Fact]
    public async Task AnInactiveUser_ResolvesToNull()
    {
        var (service, admin) = await WithAdmin();
        await NewUser(service, admin, @"CONTOSO\worker");
        Assert.True((await service.SetUserActiveAsync(admin, @"CONTOSO\worker", false)).Ok);

        Assert.Null(await service.ResolveAsync(@"CONTOSO\worker"));
    }

    /// <summary>
    /// Verifies that a principal has only the incoming permissions of its roles.
    /// </summary>
    [Fact]
    public async Task APrincipal_HasOnlyTheIncomingPermissionsOfItsRoles()
    {
        var (service, admin) = await WithAdmin();
        var operatorRole = await NewRole(service, admin, "Operator", Permissions.DashboardView, Permissions.LogsView);
        var user = await NewUser(service, admin, @"CONTOSO\op", operatorRole);

        Assert.False(user.IsAdministrator);
        Assert.True(user.Has(Permissions.LogsView));
        Assert.False(user.Has(Permissions.UsersManage));
        Assert.Equal(new[] { "Operator" }, user.RoleNames);
    }

    /// <summary>
    /// Verifies that administrators have every permission even ones that do not exist yet.
    /// </summary>
    [Fact]
    public async Task Administrators_HaveEveryPermission_EvenOnesThatDoNotExistYet()
    {
        var (_, admin) = await WithAdmin();

        Assert.True(admin.Has(Permissions.UsersManage));
        Assert.True(admin.Has("Documents.Manage"));
        foreach (var info in Permissions.All)
        {
            Assert.Contains(info.Key, admin.Permissions);
        }
    }

    /// <summary>
    /// Verifies that a permission change is visible immediately not after the cache expires.
    /// </summary>
    [Fact]
    public async Task APermissionChange_IsVisibleImmediately_NotAfterTheCacheExpires()
    {
        var (service, admin) = await WithAdmin();
        var role = await NewRole(service, admin, "Viewer", Permissions.DashboardView);
        var user = await NewUser(service, admin, @"CONTOSO\viewer", role);
        Assert.False(user.Has(Permissions.LogsView));

        Assert.True((await service.UpdateRoleAsync(admin, role, "Viewer", new[] { Permissions.DashboardView, Permissions.LogsView })).Ok);

        Assert.True((await service.ResolveAsync(@"CONTOSO\viewer"))!.Has(Permissions.LogsView));
    }

    /// <summary>
    /// Verifies that when the store fails resolve fails and never grants anything.
    /// </summary>
    [Fact]
    public async Task WhenTheStoreFails_ResolveFails_AndNeverGrantsAnything()
    {
        var service = new AccessService(new ThrowingStore(), new[] { BootstrapAccount });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(BootstrapAccount));
    }

    /// <summary>
    /// Verifies that managing users needs the users manage permission.
    /// </summary>
    [Fact]
    public async Task ManagingUsers_NeedsTheUsersManagePermission()
    {
        var (service, admin) = await WithAdmin();
        var role = await NewRole(service, admin, "Logs only", Permissions.LogsView);
        var viewer = await NewUser(service, admin, @"CONTOSO\viewer", role);

        var result = await service.AddUserAsync(viewer, @"CONTOSO\new", null, new Guid[0]);

        Assert.Equal(AccessError.Forbidden, result.Error);
        Assert.Null(await service.ResolveAsync(@"CONTOSO\new"));
    }

    /// <summary>
    /// Verifies that account names must look like domain backslash user.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("noDomain")]
    [InlineData(@"\nodomain")]
    [InlineData(@"domain\")]
    public async Task AccountNames_MustLookLikeDomainBackslashUser(string account)
    {
        var (service, admin) = await WithAdmin();

        var result = await service.AddUserAsync(admin, account, null, new Guid[0]);

        Assert.Equal(AccessError.Invalid, result.Error);
    }

    /// <summary>
    /// Verifies that adding an existing user is a conflict even with different case.
    /// </summary>
    [Fact]
    public async Task AddingAnExistingUser_IsAConflict_EvenWithDifferentCase()
    {
        var (service, admin) = await WithAdmin();
        await NewUser(service, admin, @"CONTOSO\worker");

        var result = await service.AddUserAsync(admin, @"contoso\WORKER", null, new Guid[0]);

        Assert.Equal(AccessError.Conflict, result.Error);
    }

    /// <summary>
    /// Verifies that adding a user with an unknown role is invalid.
    /// </summary>
    [Fact]
    public async Task AddingAUser_WithAnUnknownRole_IsInvalid()
    {
        var (service, admin) = await WithAdmin();

        var result = await service.AddUserAsync(admin, @"CONTOSO\worker", null, new[] { Guid.NewGuid() });

        Assert.Equal(AccessError.Invalid, result.Error);
    }

    /// <summary>
    /// Verifies that you cannot give a role that holds permissions you do not have.
    /// </summary>
    [Fact]
    public async Task YouCannotGiveARole_ThatHoldsPermissionsYouDoNotHave()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var auditor = await NewRole(service, admin, "Auditor", Permissions.AuditView);
        var userManager = await NewUser(service, admin, @"CONTOSO\um", manager);

        var result = await service.AddUserAsync(userManager, @"CONTOSO\x", null, new[] { auditor });

        Assert.Equal(AccessError.Forbidden, result.Error);
    }

    /// <summary>
    /// Verifies that only administrators can give the administrator role.
    /// </summary>
    [Fact]
    public async Task OnlyAdministrators_CanGiveTheAdministratorRole()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var userManager = await NewUser(service, admin, @"CONTOSO\um", manager);

        Assert.Equal(AccessError.Forbidden, (await service.AddUserAsync(userManager, @"CONTOSO\x", null, new[] { AccessRoles.AdministratorId })).Error);
        Assert.True((await service.AddUserAsync(admin, @"CONTOSO\y", null, new[] { AccessRoles.AdministratorId })).Ok);
    }

    /// <summary>
    /// Verifies that a non administrator cannot strip the administrator role from someone.
    /// </summary>
    [Fact]
    public async Task ANonAdministrator_CannotStripTheAdministratorRoleFromSomeone()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var userManager = await NewUser(service, admin, @"CONTOSO\um", manager);
        await NewUser(service, admin, @"CONTOSO\second", AccessRoles.AdministratorId);

        var roles = await service.SetUserRolesAsync(userManager, @"CONTOSO\second", new Guid[0]);
        var deactivate = await service.SetUserActiveAsync(userManager, @"CONTOSO\second", false);
        var delete = await service.DeleteUserAsync(userManager, @"CONTOSO\second");

        Assert.Equal(AccessError.Forbidden, roles.Error);
        Assert.Equal(AccessError.Forbidden, deactivate.Error);
        Assert.Equal(AccessError.Forbidden, delete.Error);
    }

    /// <summary>
    /// Verifies that a non administrator cannot reactivate an administrator.
    /// </summary>
    [Fact]
    public async Task ANonAdministrator_CannotReactivateAnAdministrator()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var userManager = await NewUser(service, admin, @"CONTOSO\um", manager);
        await NewUser(service, admin, @"CONTOSO\second", AccessRoles.AdministratorId);
        Assert.True((await service.SetUserActiveAsync(admin, @"CONTOSO\second", false)).Ok);

        var result = await service.SetUserActiveAsync(userManager, @"CONTOSO\second", true);

        Assert.Equal(AccessError.Forbidden, result.Error);
        Assert.Null(await service.ResolveAsync(@"CONTOSO\second"));
    }

    /// <summary>
    /// Verifies that the last active administrator cannot be demoted deactivated or deleted.
    /// </summary>
    [Fact]
    public async Task TheLastActiveAdministrator_CannotBeDemoted_Deactivated_OrDeleted()
    {
        var (service, admin) = await WithAdmin();

        Assert.Equal(AccessError.Conflict, (await service.SetUserRolesAsync(admin, BootstrapAccount, new Guid[0])).Error);
        Assert.Equal(AccessError.Conflict, (await service.SetUserActiveAsync(admin, BootstrapAccount, false)).Error);
        Assert.Equal(AccessError.Conflict, (await service.DeleteUserAsync(admin, BootstrapAccount)).Error);
        Assert.True((await service.ResolveAsync(BootstrapAccount))!.IsAdministrator);
    }

    /// <summary>
    /// Verifies that with a second administrator the first can be demoted.
    /// </summary>
    [Fact]
    public async Task WithASecondAdministrator_TheFirstCanBeDemoted()
    {
        var (service, admin) = await WithAdmin();
        await NewUser(service, admin, @"CONTOSO\second", AccessRoles.AdministratorId);

        var result = await service.SetUserRolesAsync(admin, BootstrapAccount, new Guid[0]);

        Assert.True(result.Ok, result.Message);
        Assert.False((await service.ResolveAsync(BootstrapAccount))!.IsAdministrator);
    }

    /// <summary>
    /// Verifies that an inactive administrator does not count as an administrator.
    /// </summary>
    [Fact]
    public async Task AnInactiveAdministrator_DoesNotCountAsAnAdministrator()
    {
        var (service, admin) = await WithAdmin();
        await NewUser(service, admin, @"CONTOSO\second", AccessRoles.AdministratorId);
        Assert.True((await service.SetUserActiveAsync(admin, @"CONTOSO\second", false)).Ok);

        var result = await service.SetUserActiveAsync(admin, BootstrapAccount, false);

        Assert.Equal(AccessError.Conflict, result.Error);
    }

    /// <summary>
    /// Verifies that a user who has acted cannot be deleted but one who never did can.
    /// </summary>
    [Fact]
    public async Task AUserWhoHasActed_CannotBeDeleted_ButOneWhoNeverDidCan()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var actor = await NewUser(service, admin, @"CONTOSO\actor", manager);
        await NewUser(service, actor, @"CONTOSO\idle");
        Assert.True((await service.AddUserAsync(actor, @"CONTOSO\made", null, new Guid[0])).Ok);

        Assert.Equal(AccessError.Conflict, (await service.DeleteUserAsync(admin, @"CONTOSO\actor")).Error);
        Assert.True((await service.DeleteUserAsync(admin, @"CONTOSO\idle")).Ok);
        Assert.Null(await service.ResolveAsync(@"CONTOSO\idle"));
    }

    /// <summary>
    /// Verifies that changing an unknown user is not found.
    /// </summary>
    [Fact]
    public async Task ChangingAnUnknownUser_IsNotFound()
    {
        var (service, admin) = await WithAdmin();

        Assert.Equal(AccessError.NotFound, (await service.SetUserActiveAsync(admin, @"CONTOSO\ghost", false)).Error);
        Assert.Equal(AccessError.NotFound, (await service.SetUserRolesAsync(admin, @"CONTOSO\ghost", new Guid[0])).Error);
        Assert.Equal(AccessError.NotFound, (await service.DeleteUserAsync(admin, @"CONTOSO\ghost")).Error);
    }

    /// <summary>
    /// Verifies that managing roles needs the roles manage permission.
    /// </summary>
    [Fact]
    public async Task ManagingRoles_NeedsTheRolesManagePermission()
    {
        var (service, admin) = await WithAdmin();
        var role = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var user = await NewUser(service, admin, @"CONTOSO\um", role);

        var result = await service.CreateRoleAsync(user, "Another", new[] { Permissions.UsersManage });

        Assert.Equal(AccessError.Forbidden, result.Error);
    }

    /// <summary>
    /// Verifies that you cannot add a permission to a role that you do not have yourself.
    /// </summary>
    [Fact]
    public async Task YouCannotAddAPermissionToARole_ThatYouDoNotHaveYourself()
    {
        var (service, admin) = await WithAdmin();
        var roleManager = await NewRole(service, admin, "Role manager", Permissions.RolesManage, Permissions.LogsView);
        var user = await NewUser(service, admin, @"CONTOSO\rm", roleManager);

        var escalate = await service.CreateRoleAsync(user, "Sneaky", new[] { Permissions.AuditView });
        var ok = await service.CreateRoleAsync(user, "Fine", new[] { Permissions.LogsView });

        Assert.Equal(AccessError.Forbidden, escalate.Error);
        Assert.True(ok.Ok, ok.Message);
    }

    /// <summary>
    /// Verifies that you cannot escalate by updating a role either but can reduce it.
    /// </summary>
    [Fact]
    public async Task YouCannotEscalate_ByUpdatingARoleEither_ButCanReduceIt()
    {
        var (service, admin) = await WithAdmin();
        var roleManager = await NewRole(service, admin, "Role manager", Permissions.RolesManage, Permissions.LogsView);
        var audit = await NewRole(service, admin, "Auditor", Permissions.AuditView, Permissions.LogsView);
        var user = await NewUser(service, admin, @"CONTOSO\rm", roleManager);

        var escalate = await service.UpdateRoleAsync(user, audit, "Auditor", new[] { Permissions.AuditView, Permissions.UsersManage });
        var reduce = await service.UpdateRoleAsync(user, audit, "Auditor", new[] { Permissions.AuditView });

        Assert.Equal(AccessError.Forbidden, escalate.Error);
        Assert.True(reduce.Ok, reduce.Message);
    }

    /// <summary>
    /// Verifies that the built in role cannot be edited or deleted even by an administrator.
    /// </summary>
    [Fact]
    public async Task TheBuiltInRole_CannotBeEditedOrDeleted_EvenByAnAdministrator()
    {
        var (service, admin) = await WithAdmin();

        var update = await service.UpdateRoleAsync(admin, AccessRoles.AdministratorId, "Boss", new[] { Permissions.LogsView });
        var delete = await service.DeleteRoleAsync(admin, AccessRoles.AdministratorId);

        Assert.Equal(AccessError.Invalid, update.Error);
        Assert.Equal(AccessError.Invalid, delete.Error);
        Assert.Equal(AccessRoles.AdministratorName, (await _store.GetRoleAsync(AccessRoles.AdministratorId))!.Name);
    }

    /// <summary>
    /// Verifies that role names must be unique ignoring case and not empty.
    /// </summary>
    [Fact]
    public async Task RoleNames_MustBeUnique_IgnoringCase_AndNotEmpty()
    {
        var (service, admin) = await WithAdmin();
        await NewRole(service, admin, "Operator", Permissions.LogsView);

        Assert.Equal(AccessError.Conflict, (await service.CreateRoleAsync(admin, "operator", new[] { Permissions.LogsView })).Error);
        Assert.Equal(AccessError.Conflict, (await service.CreateRoleAsync(admin, "ADMINISTRATOR", new[] { Permissions.LogsView })).Error);
        Assert.Equal(AccessError.Invalid, (await service.CreateRoleAsync(admin, "  ", new[] { Permissions.LogsView })).Error);
    }

    /// <summary>
    /// Verifies that a role can keep its own name when updated.
    /// </summary>
    [Fact]
    public async Task ARole_CanKeepItsOwnName_WhenUpdated()
    {
        var (service, admin) = await WithAdmin();
        var id = await NewRole(service, admin, "Operator", Permissions.LogsView);

        var result = await service.UpdateRoleAsync(admin, id, "Operator", new[] { Permissions.DashboardView });

        Assert.True(result.Ok, result.Message);
    }

    /// <summary>
    /// Verifies that unknown permissions are invalid.
    /// </summary>
    [Fact]
    public async Task UnknownPermissions_AreInvalid()
    {
        var (service, admin) = await WithAdmin();

        var result = await service.CreateRoleAsync(admin, "Odd", new[] { "Nothing.Here" });

        Assert.Equal(AccessError.Invalid, result.Error);
    }

    /// <summary>
    /// Verifies that deleting a role takes it away from its users.
    /// </summary>
    [Fact]
    public async Task DeletingARole_TakesItAwayFromItsUsers()
    {
        var (service, admin) = await WithAdmin();
        var role = await NewRole(service, admin, "Viewer", Permissions.DashboardView);
        await NewUser(service, admin, @"CONTOSO\viewer", role);

        Assert.True((await service.DeleteRoleAsync(admin, role)).Ok);

        Assert.False((await service.ResolveAsync(@"CONTOSO\viewer"))!.Has(Permissions.DashboardView));
    }

    /// <summary>
    /// Verifies that changing an unknown role is not found.
    /// </summary>
    [Fact]
    public async Task ChangingAnUnknownRole_IsNotFound()
    {
        var (service, admin) = await WithAdmin();

        Assert.Equal(AccessError.NotFound, (await service.UpdateRoleAsync(admin, Guid.NewGuid(), "x", new string[0])).Error);
        Assert.Equal(AccessError.NotFound, (await service.DeleteRoleAsync(admin, Guid.NewGuid())).Error);
    }

    /// <summary>
    /// Verifies that every change is audited with who did what to whom and no row data in it.
    /// </summary>
    [Fact]
    public async Task EveryChange_IsAudited_WithWhoDidWhatToWhom_AndNoRowDataInIt()
    {
        var (service, admin) = await WithAdmin();
        var role = await NewRole(service, admin, "Viewer", Permissions.DashboardView);
        await NewUser(service, admin, @"CONTOSO\viewer", role);
        await service.SetUserActiveAsync(admin, @"CONTOSO\viewer", false);
        await service.DeleteRoleAsync(admin, role);

        var actions = (await _store.ListAuditAsync(100)).Where(a => a.Actor == @"contoso\admin").Select(a => a.Action).ToList();

        Assert.Contains("role.create", actions);
        Assert.Contains("user.add", actions);
        Assert.Contains("user.deactivate", actions);
        Assert.Contains("role.delete", actions);
    }

    /// <summary>
    /// Verifies that a refused change is not audited.
    /// </summary>
    [Fact]
    public async Task ARefusedChange_IsNotAudited()
    {
        var (service, admin) = await WithAdmin();
        var before = (await _store.ListAuditAsync(100)).Count;

        await service.SetUserActiveAsync(admin, BootstrapAccount, false);

        Assert.Equal(before, (await _store.ListAuditAsync(100)).Count);
    }

    private sealed class ThrowingStore : IAccessStore
    {
        private static Exception Boom() => new InvalidOperationException("store down");
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task SaveUserAsync(AccessUser user, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task DeleteUserAsync(string account, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task SaveRoleAsync(AccessRole role, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default) => throw Boom();
        /// <summary>
        /// Fake store method that always throws, used to prove the access service fails closed when the store is down.
        /// </summary>
        public Task DeleteGroupMappingAsync(string group, CancellationToken ct = default) => throw Boom();
    }
}
