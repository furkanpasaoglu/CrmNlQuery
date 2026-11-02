using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Tests.Application.Access;

/// <summary>
/// Tests that AD group to role mappings grant a group member the group's roles even when unregistered.
/// </summary>
public class GroupAccessTests
{
    private const string Boss = @"CONTOSO\Admin";
    private const string Analysts = @"CONTOSO\Analysts";

    private readonly MemoryAccessStore _store = new MemoryAccessStore();

    private static Func<string, bool> Member(params string[] groups)
        => group => groups.Contains(group, StringComparer.OrdinalIgnoreCase);

    private async Task<(AccessService Service, AccessPrincipal Admin)> WithAdmin()
    {
        var service = new AccessService(_store, new[] { Boss });
        await service.EnsureBootstrapAsync();
        return (service, (await service.ResolveAsync(Boss))!);
    }

    private static async Task<Guid> NewRole(AccessService service, AccessPrincipal admin, string name, params string[] permissions)
    {
        var result = await service.CreateRoleAsync(admin, name, permissions);
        Assert.True(result.Ok, result.Message);
        return result.Id!.Value;
    }

    /// <summary>
    /// Verifies that a member of a mapped group gets the group's permissions without being registered.
    /// </summary>
    [Fact]
    public async Task AMemberOfAMappedGroup_GetsTheGroupsPermissions_WithoutBeingRegistered()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        Assert.True((await service.SetGroupRolesAsync(admin, Analysts, new[] { logs })).Ok);

        var principal = await service.ResolveAsync(@"CONTOSO\newbie", isMember: Member(Analysts));

        Assert.NotNull(principal);
        Assert.True(principal!.Has(Permissions.LogsView));
        Assert.False(principal.Has(Permissions.UsersManage));
        Assert.False(principal.IsAdministrator);
        Assert.Equal(new[] { "Logs" }, principal.RoleNames);
        Assert.Equal(@"contoso\newbie", principal.Account);
        Assert.Equal(@"CONTOSO\newbie", principal.DisplayName);
    }

    /// <summary>
    /// Verifies that a user who is not a member of the group stays out.
    /// </summary>
    [Fact]
    public async Task ANonMember_StaysOut()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        await service.SetGroupRolesAsync(admin, Analysts, new[] { logs });

        Assert.Null(await service.ResolveAsync(@"CONTOSO\newbie", isMember: Member(@"CONTOSO\Other")));
    }

    /// <summary>
    /// Verifies that group mappings are ignored when no membership information is available.
    /// </summary>
    [Fact]
    public async Task WithoutMembershipInformation_GroupsAreIgnored()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        await service.SetGroupRolesAsync(admin, Analysts, new[] { logs });

        Assert.Null(await service.ResolveAsync(@"CONTOSO\newbie"));
    }

    /// <summary>
    /// Verifies that an explicitly deactivated user stays out even if their group would admit them.
    /// </summary>
    [Fact]
    public async Task AnExplicitlyDeactivatedUser_StaysOut_EvenIfTheirGroupWouldLetThemIn()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        await service.SetGroupRolesAsync(admin, Analysts, new[] { logs });
        await service.AddUserAsync(admin, @"CONTOSO\blocked", null, new Guid[0]);
        await service.SetUserActiveAsync(admin, @"CONTOSO\blocked", false);

        Assert.Null(await service.ResolveAsync(@"CONTOSO\blocked", isMember: Member(Analysts)));
    }

    /// <summary>
    /// Verifies that a registered user gets the union of direct and group roles.
    /// </summary>
    [Fact]
    public async Task ARegisteredUser_GetsTheUnionOfDirectAndGroupRoles()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        var audit = await NewRole(service, admin, "Audit", Permissions.AuditView);
        await service.SetGroupRolesAsync(admin, Analysts, new[] { audit });
        await service.AddUserAsync(admin, @"CONTOSO\both", "Both", new[] { logs });

        var principal = await service.ResolveAsync(@"CONTOSO\both", isMember: Member(Analysts));

        Assert.True(principal!.Has(Permissions.LogsView));
        Assert.True(principal.Has(Permissions.AuditView));
        Assert.Equal(new[] { "Audit", "Logs" }, principal.RoleNames);
        Assert.Equal("Both", principal.DisplayName);
    }

    /// <summary>
    /// Verifies that a group mapped to administrator makes its members administrators.
    /// </summary>
    [Fact]
    public async Task AGroupMappedToAdministrator_MakesItsMembersAdministrators()
    {
        var (service, admin) = await WithAdmin();
        await service.SetGroupRolesAsync(admin, Analysts, new[] { AccessRoles.AdministratorId }, default, confirmed: true);

        var principal = await service.ResolveAsync(@"CONTOSO\boss2", isMember: Member(Analysts));

        Assert.True(principal!.IsAdministrator);
        Assert.True(principal.Has("Anything.Future"));
    }

    /// <summary>
    /// Verifies that a group mapping change takes effect immediately even after a negative answer was cached.
    /// </summary>
    [Fact]
    public async Task ChangingAGroupMapping_TakesEffectImmediately_EvenAfterANegativeAnswerWasCached()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        Assert.Null(await service.ResolveAsync(@"CONTOSO\newbie", isMember: Member(Analysts)));

        await service.SetGroupRolesAsync(admin, Analysts, new[] { logs });
        Assert.NotNull(await service.ResolveAsync(@"CONTOSO\newbie", isMember: Member(Analysts)));

        await service.SetGroupRolesAsync(admin, Analysts, new Guid[0]);
        Assert.Null(await service.ResolveAsync(@"CONTOSO\newbie", isMember: Member(Analysts)));
    }

    /// <summary>
    /// Verifies that mapping groups requires the users manage permission.
    /// </summary>
    [Fact]
    public async Task MappingGroups_NeedsTheUsersManagePermission()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        await service.AddUserAsync(admin, @"CONTOSO\viewer", null, new[] { logs });
        var viewer = (await service.ResolveAsync(@"CONTOSO\viewer"))!;

        var result = await service.SetGroupRolesAsync(viewer, Analysts, new[] { logs });

        Assert.Equal(AccessError.Forbidden, result.Error);
    }

    /// <summary>
    /// Verifies that you cannot map a role holding permissions you do not have.
    /// </summary>
    [Fact]
    public async Task YouCannotMapARole_ThatHoldsPermissionsYouDoNotHave()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        var audit = await NewRole(service, admin, "Audit", Permissions.AuditView);
        await service.AddUserAsync(admin, @"CONTOSO\um", null, new[] { manager });
        var userManager = (await service.ResolveAsync(@"CONTOSO\um"))!;

        Assert.Equal(AccessError.Forbidden, (await service.SetGroupRolesAsync(userManager, Analysts, new[] { audit })).Error);
        Assert.Equal(AccessError.Forbidden, (await service.SetGroupRolesAsync(userManager, Analysts, new[] { AccessRoles.AdministratorId })).Error);
        Assert.True((await service.SetGroupRolesAsync(userManager, Analysts, new[] { manager })).Ok);
    }

    /// <summary>
    /// Verifies that a non administrator cannot take an administrator role away from a group.
    /// </summary>
    [Fact]
    public async Task ANonAdministrator_CannotTakeAnAdministratorRoleAwayFromAGroup()
    {
        var (service, admin) = await WithAdmin();
        var manager = await NewRole(service, admin, "User manager", Permissions.UsersManage);
        await service.AddUserAsync(admin, @"CONTOSO\um", null, new[] { manager });
        var userManager = (await service.ResolveAsync(@"CONTOSO\um"))!;
        await service.SetGroupRolesAsync(admin, Analysts, new[] { AccessRoles.AdministratorId }, default, confirmed: true);

        var result = await service.SetGroupRolesAsync(userManager, Analysts, new Guid[0]);

        Assert.Equal(AccessError.Forbidden, result.Error);
    }

    /// <summary>
    /// Verifies that group names without a valid domain and name format are rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("NoDomain")]
    [InlineData(@"\nodomain")]
    [InlineData(@"domain\")]
    public async Task GroupNames_MustLookLikeDomainBackslashGroup(string group)
    {
        var (service, admin) = await WithAdmin();

        Assert.Equal(AccessError.Invalid, (await service.SetGroupRolesAsync(admin, group, new[] { AccessRoles.AdministratorId })).Error);
    }

    /// <summary>
    /// Verifies that mapping an unknown role is invalid.
    /// </summary>
    [Fact]
    public async Task AnUnknownRole_IsInvalid()
    {
        var (service, admin) = await WithAdmin();

        Assert.Equal(AccessError.Invalid, (await service.SetGroupRolesAsync(admin, Analysts, new[] { Guid.NewGuid() })).Error);
    }

    /// <summary>
    /// Verifies that mapping no roles removes the mapping and is audited.
    /// </summary>
    [Fact]
    public async Task MappingNoRoles_RemovesTheMapping_AndIsAudited()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        await service.SetGroupRolesAsync(admin, Analysts, new[] { logs });
        Assert.Single(await service.ListGroupMappingsAsync());

        var removed = await service.SetGroupRolesAsync(admin, Analysts, new Guid[0]);

        Assert.True(removed.Ok);
        Assert.Empty(await service.ListGroupMappingsAsync());
        var actions = (await _store.ListAuditAsync(50)).Select(a => a.Action).ToList();
        Assert.Contains("group.roles", actions);
        Assert.Contains("group.unmap", actions);
    }

    /// <summary>
    /// Verifies that group names match ignoring case, so mapping twice updates the same group.
    /// </summary>
    [Fact]
    public async Task GroupNames_AreMatchedIgnoringCase_SoMappingTwiceUpdatesTheSameGroup()
    {
        var (service, admin) = await WithAdmin();
        var logs = await NewRole(service, admin, "Logs", Permissions.LogsView);
        var audit = await NewRole(service, admin, "Audit", Permissions.AuditView);

        await service.SetGroupRolesAsync(admin, @"CONTOSO\Analysts", new[] { logs });
        await service.SetGroupRolesAsync(admin, @"contoso\ANALYSTS", new[] { audit });

        var mappings = await service.ListGroupMappingsAsync();
        Assert.Single(mappings);
        Assert.Equal(new[] { audit }, mappings[0].RoleIds);
    }

    /// <summary>
    /// Verifies that when the store fails, resolving with groups also fails and never grants anything.
    /// </summary>
    [Fact]
    public async Task WhenTheStoreFails_ResolvingWithGroupsFailsToo_NeverGrantsAnything()
    {
        var service = new AccessService(new FailingGroupStore(), new string[0]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(@"CONTOSO\x", isMember: Member(Analysts)));
    }

    private sealed class FailingGroupStore : IAccessStore
    {
        private readonly MemoryAccessStore _inner = new MemoryAccessStore();
        /// <summary>
        /// Test fake that returns the in-memory list of users.
        /// </summary>
        public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default) => _inner.ListUsersAsync(ct);
        /// <summary>
        /// Test fake that returns a user by account.
        /// </summary>
        public Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default) => _inner.GetUserAsync(account, ct);
        /// <summary>
        /// Test fake that saves a user.
        /// </summary>
        public Task SaveUserAsync(AccessUser user, CancellationToken ct = default) => _inner.SaveUserAsync(user, ct);
        /// <summary>
        /// Test fake that deletes a user by account.
        /// </summary>
        public Task DeleteUserAsync(string account, CancellationToken ct = default) => _inner.DeleteUserAsync(account, ct);
        /// <summary>
        /// Test fake that returns the in-memory list of roles.
        /// </summary>
        public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default) => _inner.ListRolesAsync(ct);
        /// <summary>
        /// Test fake that returns a role by id.
        /// </summary>
        public Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default) => _inner.GetRoleAsync(id, ct);
        /// <summary>
        /// Test fake that saves a role.
        /// </summary>
        public Task SaveRoleAsync(AccessRole role, CancellationToken ct = default) => _inner.SaveRoleAsync(role, ct);
        /// <summary>
        /// Test fake that deletes a role by id.
        /// </summary>
        public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) => _inner.DeleteRoleAsync(id, ct);
        /// <summary>
        /// Test fake that appends an audit entry.
        /// </summary>
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => _inner.AddAuditAsync(entry, ct);
        /// <summary>
        /// Test fake that returns audit entries up to the limit.
        /// </summary>
        public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default) => _inner.ListAuditAsync(limit, ct);
        /// <summary>
        /// Test fake that checks whether an account has any audit entries as actor.
        /// </summary>
        public Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default) => _inner.HasAuditByActorAsync(account, ct);
        /// <summary>
        /// Test fake that returns the group mappings.
        /// </summary>
        public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default) => throw new InvalidOperationException("store down");
        /// <summary>
        /// Test fake that saves a group mapping.
        /// </summary>
        public Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default) => throw new InvalidOperationException("store down");
        /// <summary>
        /// Test fake that deletes a group mapping.
        /// </summary>
        public Task DeleteGroupMappingAsync(string group, CancellationToken ct = default) => throw new InvalidOperationException("store down");
    }
}
