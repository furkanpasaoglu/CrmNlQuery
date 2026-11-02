using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests the App.Use gate in its off, report and on modes.
/// </summary>
public class AppUseGateTests
{
    private const string Boss = @"LAB\boss";
    private const string Endpoint = "POST /api/ask";

    private sealed class Clock
    {
        public DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Setup
    {
        public string Mode = "off";
        public AccessService Service = null!;
        public AccessPrincipal Admin = null!;
        public Clock Clock = new Clock();
        public int GroupLookups;
        public AppUseGate Gate = null!;
        public IAccessStore Store = null!;
    }

    private static async Task<Setup> MakeAsync(IAccessStore? store = null)
    {
        var s = new Setup { Store = store ?? new MemoryAccessStore() };
        s.Service = new AccessService(s.Store, new[] { Boss }, () => s.Clock.Now);
        await s.Service.EnsureBootstrapAsync();
        s.Admin = (await s.Service.ResolveAsync(Boss))!;
        s.Gate = new AppUseGate(s.Service, () => s.Mode);
        return s;
    }

    private static Func<string, bool> NoGroups => _ => false;

    private static async Task<Guid> RoleAsync(Setup s, string name, params string[] permissions)
        => (await s.Service.CreateRoleAsync(s.Admin, name, permissions)).Id!.Value;

    private async Task<AppUseDecision> Run(Setup s, string account, Func<string, bool>? member = null)
        => await s.Gate.EvaluateAsync(account, member ?? NoGroups, Endpoint, () => { s.GroupLookups++; return "LAB\\Sales, LAB\\Everyone"; });

    private static async Task<IReadOnlyList<AuditEntry>> Audit(Setup s, string action)
        => (await s.Service.ListAuditAsync(200)).Where(e => e.Action == action).ToList();

    /// <summary>
    /// Verifies that in off mode everyone is allowed and the user is not even looked up.
    /// </summary>
    [Fact]
    public async Task Off_AllowsEveryone_AndDoesNotEvenLookTheUserUp()
    {
        var s = await MakeAsync();
        s.Mode = "off";

        var decision = await Run(s, @"LAB\stranger");

        Assert.Equal(AppUseDecisionKind.Allowed, decision.Kind);
        Assert.Equal(0, s.GroupLookups);
        Assert.Empty(await Audit(s, "access.denied"));
        Assert.Empty(await Audit(s, "access.would_deny"));
    }

    /// <summary>
    /// Verifies that in on mode a user holding App.Use is allowed.
    /// </summary>
    [Fact]
    public async Task On_AllowsAUserWhoHoldsAppUse()
    {
        var s = await MakeAsync();
        var role = await RoleAsync(s, "Users", Permissions.AppUse);
        await s.Service.AddUserAsync(s.Admin, @"LAB\alice", null, new[] { role });
        s.Mode = "on";

        Assert.Equal(AppUseDecisionKind.Allowed, (await Run(s, @"LAB\alice")).Kind);
        Assert.Equal(0, s.GroupLookups);
    }

    /// <summary>
    /// Verifies that in on mode an administrator holding every permission is allowed.
    /// </summary>
    [Fact]
    public async Task On_AllowsAnAdministrator_WhoHoldsEveryPermission()
    {
        var s = await MakeAsync();
        s.Mode = "on";

        Assert.Equal(AppUseDecisionKind.Allowed, (await Run(s, Boss)).Kind);
    }

    /// <summary>
    /// Verifies that in on mode a user who gets App.Use from an AD group is allowed.
    /// </summary>
    [Fact]
    public async Task On_AllowsAUserWhoGetsAppUseFromAnAdGroup()
    {
        var s = await MakeAsync();
        var role = await RoleAsync(s, "Users", Permissions.AppUse);
        await s.Service.SetGroupRolesAsync(s.Admin, @"LAB\Staff", new[] { role });
        s.Mode = "on";

        var decision = await Run(s, @"LAB\carol", group => group == @"LAB\Staff");

        Assert.Equal(AppUseDecisionKind.Allowed, decision.Kind);
    }

    /// <summary>
    /// Verifies that in on mode an unregistered user is denied and the attempt is recorded with their groups.
    /// </summary>
    [Fact]
    public async Task On_DeniesAnUnregisteredUser_AndRecordsTheAttemptWithTheirGroups()
    {
        var s = await MakeAsync();
        s.Mode = "on";

        var decision = await Run(s, @"LAB\Stranger");

        Assert.Equal(AppUseDecisionKind.Denied, decision.Kind);
        var entry = Assert.Single(await Audit(s, "access.denied"));
        Assert.Equal(@"lab\stranger", entry.Actor);
        Assert.Equal(Endpoint, entry.Target);
        Assert.Equal(@"needs App.Use; no_access; groups: LAB\Sales, LAB\Everyone", entry.Detail);
        Assert.Equal(1, s.GroupLookups);
    }

    /// <summary>
    /// Verifies that in on mode a registered user without App.Use is denied as missing permission.
    /// </summary>
    [Fact]
    public async Task On_DeniesARegisteredUserWithoutAppUse_AsMissingPermission()
    {
        var s = await MakeAsync();
        var role = await RoleAsync(s, "Logs", Permissions.LogsView);
        await s.Service.AddUserAsync(s.Admin, @"LAB\bob", null, new[] { role });
        s.Mode = "on";

        var decision = await Run(s, @"LAB\bob");

        Assert.Equal(AppUseDecisionKind.Denied, decision.Kind);
        Assert.Contains("missing_permission", Assert.Single(await Audit(s, "access.denied")).Detail);
    }

    /// <summary>
    /// Verifies that report mode never blocks but records who would have been blocked with their groups.
    /// </summary>
    [Fact]
    public async Task Report_NeverBlocks_ButRecordsWhoWouldHaveBeenBlocked_WithTheirGroups()
    {
        var s = await MakeAsync();
        s.Mode = "report";

        var decision = await Run(s, @"LAB\stranger");

        Assert.Equal(AppUseDecisionKind.Allowed, decision.Kind);
        Assert.Empty(await Audit(s, "access.denied"));
        var entry = Assert.Single(await Audit(s, "access.would_deny"));
        Assert.Equal(@"needs App.Use; no_access; groups: LAB\Sales, LAB\Everyone", entry.Detail);
    }

    /// <summary>
    /// Verifies that report mode writes nothing for a user who may use the tool.
    /// </summary>
    [Fact]
    public async Task Report_WritesNothing_ForAUserWhoMayUseTheTool()
    {
        var s = await MakeAsync();
        s.Mode = "report";

        Assert.Equal(AppUseDecisionKind.Allowed, (await Run(s, Boss)).Kind);
        Assert.Empty(await Audit(s, "access.would_deny"));
        Assert.Equal(0, s.GroupLookups);
    }

    /// <summary>
    /// Verifies that group names are looked up only when a record is written and the record is rate-limited.
    /// </summary>
    [Fact]
    public async Task TheGroupNamesAreLookedUpOnlyWhenARecordIsWritten_AndTheRecordIsRateLimited()
    {
        var s = await MakeAsync();
        s.Mode = "report";

        await Run(s, @"LAB\stranger");
        await Run(s, @"LAB\stranger");
        await Run(s, @"LAB\stranger");
        Assert.Equal(1, s.GroupLookups);
        Assert.Single(await Audit(s, "access.would_deny"));

        s.Clock.Now = s.Clock.Now.AddMinutes(11);
        await Run(s, @"LAB\stranger");
        Assert.Equal(2, s.GroupLookups);
        Assert.Equal(2, (await Audit(s, "access.would_deny")).Count);
    }

    /// <summary>
    /// Verifies that when the store fails, on mode refuses and report mode lets the request through.
    /// </summary>
    [Fact]
    public async Task WhenTheStoreFails_OnRefuses_AndReportLetsTheRequestThrough()
    {
        var store = new ThrowingResolveStore();
        var s = await MakeAsync(store);
        store.Fail = true;

        s.Mode = "on";
        Assert.Equal(AppUseDecisionKind.Unavailable, (await Run(s, @"LAB\alice")).Kind);

        s.Mode = "report";
        Assert.Equal(AppUseDecisionKind.Allowed, (await Run(s, @"LAB\alice")).Kind);
    }

    /// <summary>
    /// Verifies that a group lookup that throws never breaks the record.
    /// </summary>
    [Fact]
    public async Task AGroupLookupThatThrows_NeverBreaksTheRecord()
    {
        var s = await MakeAsync();
        s.Mode = "on";

        var decision = await s.Gate.EvaluateAsync(@"LAB\x", NoGroups, Endpoint, () => throw new InvalidOperationException("DC unreachable"));

        Assert.Equal(AppUseDecisionKind.Denied, decision.Kind);
        Assert.Equal("needs App.Use; no_access", Assert.Single(await Audit(s, "access.denied")).Detail);
    }

    /// <summary>
    /// Verifies that an unknown mode is treated as on never as off.
    /// </summary>
    [Fact]
    public async Task AnUnknownMode_IsTreatedAsOn_NeverAsOff()
    {
        var s = await MakeAsync();
        s.Mode = "garbage";

        Assert.Equal(AppUseDecisionKind.Denied, (await Run(s, @"LAB\stranger")).Kind);
    }

    /// <summary>
    /// Verifies that a failure writing the audit record does not change the decision.
    /// </summary>
    [Fact]
    public async Task AnAuditWriteFailure_DoesNotChangeTheDecision()
    {
        var store = new ThrowingResolveStore();
        var s = await MakeAsync(store);
        store.FailAudit = true;
        s.Mode = "on";

        Assert.Equal(AppUseDecisionKind.Denied, (await Run(s, @"LAB\stranger")).Kind);
    }

    private sealed class ThrowingResolveStore : IAccessStore
    {
        private readonly MemoryAccessStore _inner = new MemoryAccessStore();
        public bool Fail { get; set; }
        public bool FailAudit { get; set; }

        private void Check() { if (Fail) { throw new InvalidOperationException("store down"); } }

        /// <summary>
        /// Test store whose lookups throw, used to simulate a failing access store; lists roles.
        /// </summary>
        public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default) { Check(); return _inner.ListRolesAsync(ct); }
        /// <summary>
        /// Test store that throws when getting a role.
        /// </summary>
        public Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default) { Check(); return _inner.GetRoleAsync(id, ct); }
        /// <summary>
        /// Test store that throws when saving a role.
        /// </summary>
        public Task SaveRoleAsync(AccessRole role, CancellationToken ct = default) { Check(); return _inner.SaveRoleAsync(role, ct); }
        /// <summary>
        /// Test store that throws when deleting a role.
        /// </summary>
        public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) { Check(); return _inner.DeleteRoleAsync(id, ct); }
        /// <summary>
        /// Test store that throws when listing users.
        /// </summary>
        public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default) { Check(); return _inner.ListUsersAsync(ct); }
        /// <summary>
        /// Test store that throws when getting a user.
        /// </summary>
        public Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default) { Check(); return _inner.GetUserAsync(account, ct); }
        /// <summary>
        /// Test store that throws when saving a user.
        /// </summary>
        public Task SaveUserAsync(AccessUser user, CancellationToken ct = default) { Check(); return _inner.SaveUserAsync(user, ct); }
        /// <summary>
        /// Test store that throws when deleting a user.
        /// </summary>
        public Task DeleteUserAsync(string account, CancellationToken ct = default) { Check(); return _inner.DeleteUserAsync(account, ct); }
        /// <summary>
        /// Test store that throws when listing group mappings.
        /// </summary>
        public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default) { Check(); return _inner.ListGroupMappingsAsync(ct); }
        /// <summary>
        /// Test store that throws when saving a group mapping.
        /// </summary>
        public Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default) { Check(); return _inner.SaveGroupMappingAsync(mapping, ct); }
        /// <summary>
        /// Test store that throws when deleting a group mapping.
        /// </summary>
        public Task DeleteGroupMappingAsync(string group, CancellationToken ct = default) { Check(); return _inner.DeleteGroupMappingAsync(group, ct); }
        /// <summary>
        /// Test store that throws when adding an audit entry.
        /// </summary>
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default)
        {
            if (FailAudit) { throw new InvalidOperationException("audit down"); }
            Check();
            return _inner.AddAuditAsync(entry, ct);
        }
        /// <summary>
        /// Test store that throws when listing audit entries.
        /// </summary>
        public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default) => _inner.ListAuditAsync(limit, ct);
        /// <summary>
        /// Test store that throws when checking for audit entries by actor.
        /// </summary>
        public Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default) => _inner.HasAuditByActorAsync(account, ct);
    }
}
