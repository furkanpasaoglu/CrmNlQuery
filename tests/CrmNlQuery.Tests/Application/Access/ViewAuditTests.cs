using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.Access;

/// <summary>
/// Tests that every viewing of sensitive records is audited, with optional rate limiting per action, person, target and filter.
/// </summary>
public class ViewAuditTests
{
    private sealed class Clock { public DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero); }

    private static (AccessService Service, Clock Clock) Make()
    {
        var clock = new Clock();
        return (new AccessService(new MemoryAccessStore(), new[] { @"LAB\boss" }, () => clock.Now), clock);
    }

    /// <summary>
    /// Verifies that a view is recorded with who what and the detail.
    /// </summary>
    [Fact]
    public async Task AView_IsRecordedWithWhoWhatAndTheDetail()
    {
        var (service, _) = Make();

        await service.RecordViewAsync("accesslog.view", @"LAB\Boss", "GET /api/admin/access-log", "key-1", "range 24h; 12 rows", rateLimit: true);

        var entry = Assert.Single(await service.ListAuditAsync(10));
        Assert.Equal("accesslog.view", entry.Action);
        Assert.Equal(@"lab\boss", entry.Actor);
        Assert.Equal("GET /api/admin/access-log", entry.Target);
        Assert.Equal("range 24h; 12 rows", entry.Detail);
    }

    /// <summary>
    /// Verifies that with the rate limit the same key is written once per ten minutes and a different key always.
    /// </summary>
    [Fact]
    public async Task WithTheRateLimit_TheSameKeyIsWrittenOncePerTenMinutes_ADifferentKeyAlways()
    {
        var (service, clock) = Make();

        await service.RecordViewAsync("accesslog.view", @"LAB\boss", "t", "same", "d", rateLimit: true);
        await service.RecordViewAsync("accesslog.view", @"LAB\boss", "t", "same", "d", rateLimit: true);
        await service.RecordViewAsync("accesslog.view", @"LAB\boss", "t", "other", "d", rateLimit: true);
        Assert.Equal(2, (await service.ListAuditAsync(10)).Count);

        clock.Now = clock.Now.AddMinutes(11);
        await service.RecordViewAsync("accesslog.view", @"LAB\boss", "t", "same", "d", rateLimit: true);
        Assert.Equal(3, (await service.ListAuditAsync(10)).Count);
    }

    /// <summary>
    /// Verifies that without the rate limit every view is written.
    /// </summary>
    [Fact]
    public async Task WithoutTheRateLimit_EveryViewIsWritten()
    {
        var (service, _) = Make();

        for (var i = 0; i < 3; i++)
        {
            await service.RecordViewAsync("questions.view", @"LAB\boss", "lab\\alice", "k", "5 questions", rateLimit: false);
        }

        Assert.Equal(3, (await service.ListAuditAsync(10)).Count);
    }
}
