using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.Access;

/// <summary>
/// Tests that denied admin attempts and metadata refreshes are audited without flooding and without values or query strings.
/// </summary>
public class DeniedAuditTests
{
    private const string Boss = @"LAB\boss";
    private const string Endpoint = "PUT /api/admin/settings";

    private sealed class Clock
    {
        public DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    }

    private static (AccessService Service, Clock Clock) Make()
    {
        var clock = new Clock();
        return (new AccessService(new MemoryAccessStore(), new[] { Boss }, () => clock.Now), clock);
    }

    private static async Task<IReadOnlyList<AuditEntry>> Denied(AccessService service)
        => (await service.ListAuditAsync(500)).Where(e => e.Action == "access.denied").ToList();

    /// <summary>
    /// Verifies that a denied attempt is recorded with account endpoint permission and reason.
    /// </summary>
    [Fact]
    public async Task ADeniedAttempt_IsRecorded_WithAccountEndpointPermissionAndReason()
    {
        var (service, _) = Make();

        await service.RecordDeniedAsync(@"LAB\Mallory", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);

        var entry = Assert.Single(await Denied(service));
        Assert.Equal(@"lab\mallory", entry.Actor);
        Assert.Equal(Endpoint, entry.Target);
        Assert.Equal("needs Settings.Manage; no_access", entry.Detail);
    }

    /// <summary>
    /// Verifies that the same attempt is recorded only once per ten minutes.
    /// </summary>
    [Fact]
    public async Task TheSameAttempt_IsRecordedOnlyOncePerTenMinutes()
    {
        var (service, clock) = Make();

        await service.RecordDeniedAsync(@"LAB\mallory", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        clock.Now = clock.Now.AddMinutes(9);
        await service.RecordDeniedAsync(@"LAB\MALLORY", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        Assert.Single(await Denied(service));

        clock.Now = clock.Now.AddMinutes(2);
        await service.RecordDeniedAsync(@"LAB\mallory", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        Assert.Equal(2, (await Denied(service)).Count);
    }

    /// <summary>
    /// Verifies that a different account endpoint or reason is counted separately.
    /// </summary>
    [Fact]
    public async Task ADifferentAccount_Endpoint_OrReason_IsCountedSeparately()
    {
        var (service, _) = Make();

        await service.RecordDeniedAsync(@"LAB\a", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        await service.RecordDeniedAsync(@"LAB\b", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        await service.RecordDeniedAsync(@"LAB\a", "GET /api/admin/users", Permissions.UsersManage, AdminAccess.NoAccess);
        await service.RecordDeniedAsync(@"LAB\a", Endpoint, Permissions.SettingsManage, AdminAccess.MissingPermission);

        Assert.Equal(4, (await Denied(service)).Count);
    }

    /// <summary>
    /// Verifies that an endpoint without a permission requirement says registration is needed.
    /// </summary>
    [Fact]
    public async Task AnEndpointWithoutAPermissionRequirement_SaysRegistrationIsNeeded()
    {
        var (service, _) = Make();

        await service.RecordDeniedAsync(@"LAB\a", "GET /api/admin/me", null, AdminAccess.NoAccess);

        Assert.Equal("needs registration; no_access", Assert.Single(await Denied(service)).Detail);
    }

    /// <summary>
    /// Verifies that many different attempts do not flood the audit above the key limit.
    /// </summary>
    [Fact]
    public async Task ManyDifferentAttempts_DoNotFloodTheAudit_AboveTheKeyLimit()
    {
        var (service, _) = Make();

        for (var i = 0; i < 5200; i++)
        {
            await service.RecordDeniedAsync(@"LAB\u" + i, Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        }

        Assert.True((await service.ListAuditAsync(500)).Count <= 500);
        await service.RecordDeniedAsync(@"LAB\late", Endpoint, Permissions.SettingsManage, AdminAccess.NoAccess);
        Assert.DoesNotContain(await Denied(service), e => e.Actor == @"lab\late");
    }

    /// <summary>
    /// Verifies that RecordAsync writes an action without any message.
    /// </summary>
    [Fact]
    public async Task RecordAsync_WritesAnActionWithoutAnyMessage()
    {
        var (service, _) = Make();

        await service.RecordAsync(@"lab\boss", "metadata.refresh", "crm metadata", "ok");

        var entry = Assert.Single(await service.ListAuditAsync(10));
        Assert.Equal(("metadata.refresh", "crm metadata", "ok"), (entry.Action, entry.Target, entry.Detail));
    }
}
