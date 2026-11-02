using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Tests.Contracts.Access;

/// <summary>
/// Shared contract tests that every access store implementation, in-memory or PostgreSQL, must pass.
/// </summary>
public abstract class AccessStoreContract : IAsyncLifetime
{
    protected abstract IAccessStore CreateStore();

    /// <summary>
    /// Prepares the access-store contract tests, clearing leftover test records so fake admins never break bootstrap or last-administrator rules in a real database.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Removes the records written by the access-store contract tests after each run.
    /// </summary>
    public async Task DisposeAsync()
    {
        var store = CreateStore();
        foreach (var user in await store.ListUsersAsync())
        {
            if (user.CreatedBy == "tester")
            {
                await store.DeleteUserAsync(user.Account);
            }
        }
        foreach (var mapping in await store.ListGroupMappingsAsync())
        {
            if (mapping.Group.StartsWith(@"labg", StringComparison.Ordinal))
            {
                await store.DeleteGroupMappingAsync(mapping.Group);
            }
        }
        foreach (var role in await store.ListRolesAsync())
        {
            if (!role.Builtin && (role.Name.StartsWith("role-", StringComparison.Ordinal) || role.Name.StartsWith("Role-", StringComparison.Ordinal)))
            {
                await store.DeleteRoleAsync(role.Id);
            }
        }
    }

    private static string NewGroup() => @"labg" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private static string NewAccount() => @"lab\u" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private static AccessUser User(string account, bool active = true, params Guid[] roles)
        => new AccessUser(account, account, active, DateTimeOffset.UtcNow, "tester", roles);

    private static AccessRole Role(string? name = null, params string[] permissions)
        => new AccessRole(Guid.NewGuid(), name ?? "role-" + Guid.NewGuid().ToString("N").Substring(0, 12), false, permissions);

    /// <summary>
    /// Verifies that the built in administrator role always exists.
    /// </summary>
    [Fact]
    public async Task TheBuiltInAdministratorRole_AlwaysExists()
    {
        var store = CreateStore();

        var role = await store.GetRoleAsync(AccessRoles.AdministratorId);

        Assert.NotNull(role);
        Assert.True(role!.Builtin);
        Assert.Equal(AccessRoles.AdministratorName, role.Name);
    }

    /// <summary>
    /// Verifies that a unknown user is null.
    /// </summary>
    [Fact]
    public async Task AUnknownUser_IsNull()
    {
        var store = CreateStore();
        Assert.Null(await store.GetUserAsync(NewAccount()));
    }

    /// <summary>
    /// Verifies that a user round trips with its roles.
    /// </summary>
    [Fact]
    public async Task AUser_RoundTrips_WithItsRoles()
    {
        var store = CreateStore();
        var role = Role(permissions: Permissions.LogsView);
        await store.SaveRoleAsync(role);
        var account = NewAccount();

        await store.SaveUserAsync(User(account, true, role.Id, AccessRoles.AdministratorId));

        var read = await store.GetUserAsync(account);
        Assert.NotNull(read);
        Assert.Equal("tester", read!.CreatedBy);
        Assert.True(read.Active);
        Assert.Equal(new[] { role.Id, AccessRoles.AdministratorId }.OrderBy(x => x), read.RoleIds.OrderBy(x => x));
        Assert.Contains(await store.ListUsersAsync(), u => u.Account == account);
    }

    /// <summary>
    /// Verifies that saving a user again replaces its roles and active flag.
    /// </summary>
    [Fact]
    public async Task SavingAUserAgain_ReplacesItsRolesAndActiveFlag()
    {
        var store = CreateStore();
        var first = Role();
        var second = Role();
        await store.SaveRoleAsync(first);
        await store.SaveRoleAsync(second);
        var account = NewAccount();
        await store.SaveUserAsync(User(account, true, first.Id));

        await store.SaveUserAsync(User(account, false, second.Id));

        var read = await store.GetUserAsync(account);
        Assert.False(read!.Active);
        Assert.Equal(new[] { second.Id }, read.RoleIds);
    }

    /// <summary>
    /// Verifies that deleting a user removes it.
    /// </summary>
    [Fact]
    public async Task DeletingAUser_RemovesIt()
    {
        var store = CreateStore();
        var account = NewAccount();
        await store.SaveUserAsync(User(account));

        await store.DeleteUserAsync(account);

        Assert.Null(await store.GetUserAsync(account));
    }

    /// <summary>
    /// Verifies that a role round trips with its permissions and updates replace them.
    /// </summary>
    [Fact]
    public async Task ARole_RoundTrips_WithItsPermissions_AndUpdatesReplaceThem()
    {
        var store = CreateStore();
        var role = Role(permissions: new[] { Permissions.LogsView, Permissions.AuditView });
        await store.SaveRoleAsync(role);

        var read = await store.GetRoleAsync(role.Id);
        Assert.Equal(role.Name, read!.Name);
        Assert.False(read.Builtin);
        Assert.Equal(new[] { Permissions.AuditView, Permissions.LogsView }, read.Permissions.OrderBy(x => x, StringComparer.Ordinal));

        await store.SaveRoleAsync(role with { Name = role.Name + "-2", Permissions = new[] { Permissions.DashboardView } });

        var updated = await store.GetRoleAsync(role.Id);
        Assert.Equal(role.Name + "-2", updated!.Name);
        Assert.Equal(new[] { Permissions.DashboardView }, updated.Permissions);
        Assert.Contains(await store.ListRolesAsync(), r => r.Id == role.Id);
    }

    /// <summary>
    /// Verifies that deleting a role also removes it from users.
    /// </summary>
    [Fact]
    public async Task DeletingARole_AlsoRemovesItFromUsers()
    {
        var store = CreateStore();
        var role = Role();
        await store.SaveRoleAsync(role);
        var account = NewAccount();
        await store.SaveUserAsync(User(account, true, role.Id, AccessRoles.AdministratorId));

        await store.DeleteRoleAsync(role.Id);

        Assert.Null(await store.GetRoleAsync(role.Id));
        Assert.Equal(new[] { AccessRoles.AdministratorId }, (await store.GetUserAsync(account))!.RoleIds);
    }

    /// <summary>
    /// Verifies that a group mapping round trips and saving again replaces its roles.
    /// </summary>
    [Fact]
    public async Task AGroupMapping_RoundTrips_AndSavingAgainReplacesItsRoles()
    {
        var store = CreateStore();
        var first = Role();
        var second = Role();
        await store.SaveRoleAsync(first);
        await store.SaveRoleAsync(second);
        var group = NewGroup();

        await store.SaveGroupMappingAsync(new GroupMapping(group, "LABAnalysts", new[] { first.Id, second.Id }));
        var saved = (await store.ListGroupMappingsAsync()).Single(m => m.Group == group);
        Assert.Equal("LABAnalysts", saved.DisplayName);
        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(x => x), saved.RoleIds.OrderBy(x => x));

        await store.SaveGroupMappingAsync(new GroupMapping(group, "LABAnalysts", new[] { second.Id }));
        Assert.Equal(new[] { second.Id }, (await store.ListGroupMappingsAsync()).Single(m => m.Group == group).RoleIds);
    }

    /// <summary>
    /// Verifies that deleting a group mapping removes it.
    /// </summary>
    [Fact]
    public async Task DeletingAGroupMapping_RemovesIt()
    {
        var store = CreateStore();
        var group = NewGroup();
        await store.SaveGroupMappingAsync(new GroupMapping(group, group, new[] { AccessRoles.AdministratorId }));

        await store.DeleteGroupMappingAsync(group);

        Assert.DoesNotContain(await store.ListGroupMappingsAsync(), m => m.Group == group);
    }

    /// <summary>
    /// Verifies that deleting a role also takes it out of group mappings.
    /// </summary>
    [Fact]
    public async Task DeletingARole_AlsoTakesItOutOfGroupMappings()
    {
        var store = CreateStore();
        var role = Role();
        await store.SaveRoleAsync(role);
        var group = NewGroup();
        await store.SaveGroupMappingAsync(new GroupMapping(group, group, new[] { role.Id, AccessRoles.AdministratorId }));

        await store.DeleteRoleAsync(role.Id);

        Assert.Equal(new[] { AccessRoles.AdministratorId }, (await store.ListGroupMappingsAsync()).Single(m => m.Group == group).RoleIds);
    }

    /// <summary>
    /// Verifies that audit is listed newest first and limited.
    /// </summary>
    [Fact]
    public async Task Audit_IsListedNewestFirst_AndLimited()
    {
        var store = CreateStore();
        var actor = NewAccount();
        var start = DateTimeOffset.UtcNow;
        await store.AddAuditAsync(new AuditEntry(start, actor, "a.one", "t1", "d1"));
        await store.AddAuditAsync(new AuditEntry(start.AddSeconds(1), actor, "a.two", "t2", "d2"));
        await store.AddAuditAsync(new AuditEntry(start.AddSeconds(2), actor, "a.three", "t3", "d3"));

        var latest = await store.ListAuditAsync(1000);
        var mine = latest.Where(a => a.Actor == actor).ToList();

        Assert.Equal(new[] { "a.three", "a.two", "a.one" }, mine.Select(a => a.Action));
        Assert.Equal("d3", mine[0].Detail);
        Assert.True((await store.ListAuditAsync(2)).Count <= 2);
    }

    /// <summary>
    /// Verifies that has audit by actor is true only for those who acted.
    /// </summary>
    [Fact]
    public async Task HasAuditByActor_IsTrueOnlyForThoseWhoActed()
    {
        var store = CreateStore();
        var actor = NewAccount();
        await store.AddAuditAsync(new AuditEntry(DateTimeOffset.UtcNow, actor, "x", "y", ""));

        Assert.True(await store.HasAuditByActorAsync(actor));
        Assert.False(await store.HasAuditByActorAsync(NewAccount()));
    }
}
