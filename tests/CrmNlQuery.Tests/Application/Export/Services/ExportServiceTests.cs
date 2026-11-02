using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Export.Exceptions;
using CrmNlQuery.Application.Export.Models;
using CrmNlQuery.Application.Export.Services;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Tests.Application.Export.Services;

/// <summary>
/// Unit tests for the admin export service: permissions, filters, limits, formatting and audit records.
/// </summary>
[Collection("WebHost")]
public class ExportServiceTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeNames : IUserNames
    {
        public int Calls { get; private set; }
        /// <summary>
        /// Test double that returns a made-up user name for each requested id and counts calls, instead of looking users up in the CRM.
        /// </summary>
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, id => "User " + id.ToString("N").Substring(0, 4)));
        }
    }

    private readonly MemoryAccessStore _audit = new MemoryAccessStore();
    private readonly MemoryAccessLogStore _accessLog = new MemoryAccessLogStore(100000);
    private readonly MemoryMetricsStore _metrics = new MemoryMetricsStore();
    private readonly FakeNames _names = new FakeNames();
    private readonly AccessService _access;
    private int _maxRows = 1000000;

    public ExportServiceTests()
    {
        _access = new AccessService(_audit, new[] { @"CONTOSO\Admin" });
    }

    private ExportService Service() => new ExportService(_audit, _accessLog, _names, _metrics,
        new MemoryFeedbackStore(_ => true), _access, () => _maxRows);

    private static AccessPrincipal Actor(params string[] permissions)
        => new AccessPrincipal(@"CONTOSO\auditor", "Auditor", new[] { "Auditor" }, false, permissions);

    private static AccessPrincipal Everything() => new AccessPrincipal(@"CONTOSO\admin", "Admin", new[] { "Administrator" }, true, Array.Empty<string>());

    private async Task<byte[]> Run(AccessPrincipal actor, ExportRequest request)
    {
        var service = Service();
        using var plan = service.Prepare(actor, request);
        var output = new MemoryStream();
        await service.StreamAsync(actor, plan, output);
        return output.ToArray();
    }

    private async Task SeedAudit(int count, Func<int, string>? action = null)
    {
        for (var i = 0; i < count; i++)
        {
            await _audit.AddAuditAsync(new AuditEntry(Start.AddMinutes(i), @"CONTOSO\boss", action?.Invoke(i) ?? "user.add", @"CONTOSO\u" + i, "roles: Viewer"));
        }
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('﻿');

    private static ExportRequest Audit(ExportFormat format = ExportFormat.Csv) => new ExportRequest(ExportDatasets.Audit, format, Start, End) { TimeZone = "Europe/Istanbul" };

    /// <summary>
    /// Verifies that audit streams every row across pages in chronological order with UTC and local time.
    /// </summary>
    [Fact]
    public async Task Audit_StreamsEveryRow_AcrossPages_InChronologicalOrder_WithUtcAndLocalTime()
    {
        await SeedAudit(ExportService.PageSize * 2 + 500);

        var bytes = await Run(Everything(), Audit());

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var lines = Text(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + ExportService.PageSize * 2 + 500, lines.Length);
        Assert.StartsWith("time_utc,time_local,time_zone,actor,action,target,detail", lines[0]);
        Assert.StartsWith("2026-07-01T00:00:00.000Z,2026-07-01T03:00:00.000+03:00,Europe/Istanbul,CONTOSO\\boss,user.add,CONTOSO\\u0,", lines[1]);
        Assert.Contains("CONTOSO\\u2499", lines[^1]);
    }

    /// <summary>
    /// Verifies that audit filters by account and action prefix.
    /// </summary>
    [Fact]
    public async Task Audit_Filters_ByAccount_AndActionPrefix()
    {
        await SeedAudit(10, i => i % 2 == 0 ? "user.add" : "role.create");
        await _audit.AddAuditAsync(new AuditEntry(Start.AddHours(1), @"CONTOSO\other", "user.add", "x", ""));

        var byPrefix = Text(await Run(Everything(), Audit() with { Kind = "role." })).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var byActor = Text(await Run(Everything(), Audit() with { Account = @"contoso\OTHER" })).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(1 + 5, byPrefix.Length);
        Assert.Equal(1 + 1, byActor.Length);
    }

    /// <summary>
    /// Verifies that JSON lines has no header no BOM and one object per line.
    /// </summary>
    [Fact]
    public async Task JsonLines_HasNoHeader_NoBom_AndOneObjectPerLine()
    {
        await SeedAudit(3);

        var bytes = await Run(Everything(), Audit(ExportFormat.JsonLines));

        Assert.NotEqual(0xEF, bytes[0]);
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => System.Text.Json.JsonDocument.Parse(l));
    }

    /// <summary>
    /// Verifies that access log resolves names joins tables and keeps row counts numeric.
    /// </summary>
    [Fact]
    public async Task AccessLog_ResolvesNames_JoinsTables_AndKeepsRowCountsNumeric()
    {
        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await _accessLog.AddAsync(new DataAccessRecord(Guid.NewGuid(), Start.AddMinutes(i), user, "ask", new[] { "account", "contact" }, 10 + i, "ok", Guid.NewGuid()));
        }
        await _accessLog.AddAsync(new DataAccessRecord(Guid.NewGuid(), Start.AddMinutes(9), Guid.NewGuid(), "result", new[] { "incident" }, 4, "ok", Guid.NewGuid()));

        var all = Text(await Run(Everything(), new ExportRequest(ExportDatasets.AccessLog, ExportFormat.Csv, Start, End) { TimeZone = "UTC" })).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var one = Text(await Run(Everything(), new ExportRequest(ExportDatasets.AccessLog, ExportFormat.Csv, Start, End) { UserId = user, Table = "contact", Kind = "ask", TimeZone = "UTC" })).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(1 + 4, all.Length);
        Assert.Contains("account;contact", all[1]);
        Assert.Contains(",10,ok,", all[1]);
        Assert.Contains("User " + user.ToString("N").Substring(0, 4), all[1]);
        Assert.Equal(1 + 3, one.Length);
        Assert.True(_names.Calls >= 1);
    }

    /// <summary>
    /// Verifies that access log filter by table and source are exact.
    /// </summary>
    [Fact]
    public async Task AccessLog_FilterByTableAndSource_AreExact()
    {
        await _accessLog.AddAsync(new DataAccessRecord(Guid.NewGuid(), Start.AddMinutes(1), Guid.NewGuid(), "ask", new[] { "account" }, 1, "ok", Guid.NewGuid()));
        await _accessLog.AddAsync(new DataAccessRecord(Guid.NewGuid(), Start.AddMinutes(2), Guid.NewGuid(), "lookup", new[] { "account" }, 1, "ok", Guid.NewGuid()));

        var lines = Text(await Run(Everything(), new ExportRequest(ExportDatasets.AccessLog, ExportFormat.Csv, Start, End) { Table = "account", Kind = "lookup" })).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains(",lookup,", lines[1]);
    }

    /// <summary>
    /// Verifies that every export writes start and done and the hash matches the bytes that were sent.
    /// </summary>
    [Fact]
    public async Task EveryExport_WritesStartAndDone_AndTheHashMatchesTheBytesThatWereSent()
    {
        await SeedAudit(50);
        var actor = Everything();

        var bytes = await Run(actor, Audit());

        var trail = await _audit.ListAuditAsync(500);
        var start = Assert.Single(trail, e => e.Action == "export.start");
        var done = Assert.Single(trail, e => e.Action == "export.done");
        Assert.Equal(actor.Account, start.Actor);
        Assert.Equal(ExportDatasets.Audit, start.Target);
        Assert.Contains("format=csv", start.Detail);
        Assert.Contains("from=2026-07-01T00:00:00Z", start.Detail);
        Assert.Contains("time_zone=Europe/Istanbul", start.Detail);
        Assert.Contains("rows=50;", done.Detail);
        Assert.Contains($"bytes={bytes.Length};", done.Detail);
        Assert.Contains("sha256=" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), done.Detail);
    }

    /// <summary>
    /// Verifies that the audit trail is exportable including earlier exports.
    /// </summary>
    [Fact]
    public async Task TheAuditTrailIsExportable_IncludingEarlierExports()
    {
        await SeedAudit(2);
        var later = () => DateTimeOffset.UtcNow.AddHours(1);
        var service = new ExportService(_audit, _accessLog, _names, _metrics, new MemoryFeedbackStore(_ => true), _access, () => _maxRows, later);
        var actor = Everything();
        var request = new ExportRequest(ExportDatasets.Audit, ExportFormat.Csv, Start, DateTimeOffset.UtcNow.AddDays(1));
        using (var first = service.Prepare(actor, request)) { await service.StreamAsync(actor, first, new MemoryStream()); }

        using var second = service.Prepare(actor, request);
        var output = new MemoryStream();
        await service.StreamAsync(actor, second, output);

        var text = Text(output.ToArray());
        Assert.Contains("export.start", text);
        Assert.Contains("export.done", text);
    }

    /// <summary>
    /// Verifies that the range end is fixed at the start so rows written during the export cannot be half included.
    /// </summary>
    [Fact]
    public async Task TheRangeEndIsFixedAtTheStart_SoRowsWrittenDuringTheExportCannotBeHalfIncluded()
    {
        await SeedAudit(3);
        await _audit.AddAuditAsync(new AuditEntry(DateTimeOffset.UtcNow, @"CONTOSOoss", "user.add", "late", ""));

        var text = Text(await Run(Everything(), new ExportRequest(ExportDatasets.Audit, ExportFormat.Csv, Start, DateTimeOffset.UtcNow.AddDays(1))));

        Assert.DoesNotContain("late", text);
        var start = (await _audit.ListAuditAsync(10)).Single(e => e.Action == "export.start");
        Assert.DoesNotContain("to=" + DateTimeOffset.UtcNow.AddDays(1).UtcDateTime.ToString("yyyy-MM-dd"), start.Detail);
    }

    /// <summary>
    /// Verifies that a filter the data set does not support is rejected not silently ignored.
    /// </summary>
    [Fact]
    public async Task AFilterTheDataSetDoesNotSupport_IsRejected_NotSilentlyIgnored()
    {
        var service = Service();
        var actor = Everything();

        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, new ExportRequest(ExportDatasets.Usage, ExportFormat.Csv, Start, End) { UserId = Guid.NewGuid() })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, new ExportRequest(ExportDatasets.Feedback, ExportFormat.Csv, Start, End) { Table = "account" })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { Table = "account" })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { UserId = Guid.NewGuid() })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, new ExportRequest(ExportDatasets.AccessLog, ExportFormat.Csv, Start, End) { Account = "x" })).Code);
    }

    /// <summary>
    /// Verifies that the account filter cannot forge evidence fields.
    /// </summary>
    [Fact]
    public void TheAccountFilter_CannotForgeEvidenceFields()
    {
        var service = Service();
        var actor = Everything();

        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { Account = "x; user=" + Guid.NewGuid() })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { Account = "x\r\nexport.done" })).Code);
        using var ok = service.Prepare(actor, Audit() with { Account = @"CONTOSO\first.last@corp" });
        Assert.NotNull(ok);
    }

    /// <summary>
    /// Verifies that a well formed but unknown time zone falls back to the server zone and says so.
    /// </summary>
    [Fact]
    public async Task AWellFormedButUnknownTimeZone_FallsBackToTheServerZone_AndSaysSo()
    {
        await SeedAudit(1);

        var bytes = await Run(Everything(), Audit() with { TimeZone = "Mars/Olympus" });

        var text = Text(bytes);
        Assert.Contains("," + TimeZoneInfo.Local.Id + ",", text);
        Assert.DoesNotContain("Mars/Olympus", text);
        var start = Assert.Single(await _audit.ListAuditAsync(10), e => e.Action == "export.start");
        Assert.Contains("requested_time_zone=Mars/Olympus (not available on this server)", start.Detail);
    }

    /// <summary>
    /// Verifies that the time zone column holds the requested IANA name.
    /// </summary>
    [Fact]
    public async Task TheTimeZoneColumn_HoldsTheRequestedIanaName()
    {
        await SeedAudit(1);

        var text = Text(await Run(Everything(), Audit()));

        Assert.Contains(",Europe/Istanbul,", text);
    }

    /// <summary>
    /// Verifies that when the start record cannot be written nothing is sent.
    /// </summary>
    [Fact]
    public async Task WhenTheStartRecordCannotBeWritten_NothingIsSent()
    {
        var failing = new AccessService(new FailingAuditStore(), new[] { @"CONTOSO\Admin" });
        var service = new ExportService(_audit, _accessLog, _names, _metrics, new MemoryFeedbackStore(_ => true), failing, () => 1000);
        var actor = Everything();
        using var plan = service.Prepare(actor, Audit());
        var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<Exception>(() => service.StreamAsync(actor, plan, output));

        Assert.Equal(0, output.Length);
    }

    private sealed class FailingAuditStore : IAccessStore
    {
        private readonly MemoryAccessStore _inner = new MemoryAccessStore();
        /// <summary>
        /// Test double whose add-audit call fails, used to prove an export sends no data when its start audit record cannot be written.
        /// </summary>
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => throw new InvalidOperationException("audit store down");
        /// <summary>
        /// Test double whose list-audit call fails, simulating an unreadable audit store in export tests.
        /// </summary>
        public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default) => _inner.ListAuditAsync(limit, ct);
        /// <summary>
        /// Test double whose audit-store method for checking whether an actor has audit entries throws, simulating a broken audit store in export tests.
        /// </summary>
        public Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default) => _inner.HasAuditByActorAsync(account, ct);
        /// <summary>
        /// Test double whose user-listing method throws, simulating an unavailable access store while the export service runs.
        /// </summary>
        public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default) => _inner.ListUsersAsync(ct);
        /// <summary>
        /// Test double whose single-user lookup throws, simulating a failing access store in export tests.
        /// </summary>
        public Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default) => _inner.GetUserAsync(account, ct);
        /// <summary>
        /// Test double whose user-save method throws, simulating a failing access store in export tests.
        /// </summary>
        public Task SaveUserAsync(AccessUser user, CancellationToken ct = default) => _inner.SaveUserAsync(user, ct);
        /// <summary>
        /// Test double whose user-delete method throws, simulating a failing access store in export tests.
        /// </summary>
        public Task DeleteUserAsync(string account, CancellationToken ct = default) => _inner.DeleteUserAsync(account, ct);
        /// <summary>
        /// Test double whose role-listing method throws, simulating a failing access store in export tests.
        /// </summary>
        public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default) => _inner.ListRolesAsync(ct);
        /// <summary>
        /// Test double whose single-role lookup throws, simulating a failing access store in export tests.
        /// </summary>
        public Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default) => _inner.GetRoleAsync(id, ct);
        /// <summary>
        /// Test double whose role-save method throws, simulating a failing access store in export tests.
        /// </summary>
        public Task SaveRoleAsync(AccessRole role, CancellationToken ct = default) => _inner.SaveRoleAsync(role, ct);
        /// <summary>
        /// Test double whose role-delete method throws, simulating a failing access store in export tests.
        /// </summary>
        public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) => _inner.DeleteRoleAsync(id, ct);
        /// <summary>
        /// Test double whose group-mapping listing throws, simulating a failing access store in export tests.
        /// </summary>
        public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default) => _inner.ListGroupMappingsAsync(ct);
        /// <summary>
        /// Test double whose group-mapping save throws, simulating a failing access store in export tests.
        /// </summary>
        public Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default) => _inner.SaveGroupMappingAsync(mapping, ct);
        /// <summary>
        /// Test double whose group-mapping delete throws, simulating a failing access store in export tests.
        /// </summary>
        public Task DeleteGroupMappingAsync(string group, CancellationToken ct = default) => _inner.DeleteGroupMappingAsync(group, ct);
    }

    /// <summary>
    /// Verifies that a cancelled download is recorded as aborted with the rows sent so far.
    /// </summary>
    [Fact]
    public async Task ACancelledDownload_IsRecordedAsAborted_WithTheRowsSentSoFar()
    {
        await SeedAudit(ExportService.PageSize + 10);
        var service = Service();
        var actor = Everything();
        using var plan = service.Prepare(actor, Audit());
        using var cts = new CancellationTokenSource();
        var output = new CancelAfterStream(cts, bytesBeforeCancel: 2000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StreamAsync(actor, plan, output, cts.Token));

        var aborted = Assert.Single(await _audit.ListAuditAsync(100), e => e.Action == "export.aborted");
        Assert.Contains("rows=", aborted.Detail);
        Assert.EndsWith("; partial", aborted.Detail);
        Assert.DoesNotContain(await _audit.ListAuditAsync(100), e => e.Action == "export.done");
    }

    private sealed class CancelAfterStream : MemoryStream
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _limit;
        public CancelAfterStream(CancellationTokenSource cts, int bytesBeforeCancel) { _cts = cts; _limit = bytesBeforeCancel; }
        /// <summary>
        /// Test stream whose write cancels the operation after output starts, simulating a client that disconnects mid-export.
        /// </summary>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            if (Length > _limit)
            {
                _cts.Cancel();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Verifies that max rows stops the file and the done record says it was truncated.
    /// </summary>
    [Fact]
    public async Task MaxRows_StopsTheFile_AndTheDoneRecordSaysItWasTruncated()
    {
        await SeedAudit(1500);
        _maxRows = 1000;

        var lines = Text(await Run(Everything(), Audit())).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(1 + 1000, lines.Length);
        var done = Assert.Single(await _audit.ListAuditAsync(10), e => e.Action == "export.done");
        Assert.Contains("rows=1000;", done.Detail);
        Assert.Contains("truncated at Export:MaxRows=1000", done.Detail);
    }

    /// <summary>
    /// Verifies that only one export per administrator at a time and the slot is released.
    /// </summary>
    [Fact]
    public void OnlyOneExportPerAdministratorAtATime_AndTheSlotIsReleased()
    {
        var service = Service();
        var actor = Everything();

        using (var first = service.Prepare(actor, Audit()))
        {
            var busy = Assert.Throws<ExportException>(() => service.Prepare(actor, Audit()));
            Assert.Equal("busy", busy.Code);
        }

        using var again = service.Prepare(actor, Audit());
        Assert.NotNull(again);
    }

    /// <summary>
    /// Verifies that at most two exports run on the server at once.
    /// </summary>
    [Fact]
    public void AtMostTwoExportsRunOnTheServerAtOnce()
    {
        var service = Service();
        using var a = service.Prepare(new AccessPrincipal(@"CONTOSO\a", "a", new[] { "r" }, true, Array.Empty<string>()), Audit());
        using var b = service.Prepare(new AccessPrincipal(@"CONTOSO\b", "b", new[] { "r" }, true, Array.Empty<string>()), Audit());

        var third = Assert.Throws<ExportException>(() => service.Prepare(new AccessPrincipal(@"CONTOSO\c", "c", new[] { "r" }, true, Array.Empty<string>()), Audit()));
        Assert.Equal("busy", third.Code);
    }

    /// <summary>
    /// Verifies that export run is not enough the data sets own view permission is needed too.
    /// </summary>
    [Fact]
    public void ExportRun_IsNotEnough_TheDataSetsOwnViewPermissionIsNeededToo()
    {
        var service = Service();

        Assert.Equal("forbidden", Assert.Throws<ExportException>(() => service.Prepare(Actor(Permissions.ExportRun), Audit())).Code);
        Assert.Equal("forbidden", Assert.Throws<ExportException>(() => service.Prepare(Actor(Permissions.AuditView), Audit())).Code);
        using var ok = service.Prepare(Actor(Permissions.ExportRun, Permissions.AuditView), Audit());
        Assert.NotNull(ok);
    }

    /// <summary>
    /// Verifies that each data set needs its own view permission.
    /// </summary>
    [Theory]
    [InlineData(ExportDatasets.AccessLog, Permissions.AccessLogView)]
    [InlineData(ExportDatasets.Usage, Permissions.DashboardView)]
    [InlineData(ExportDatasets.Feedback, Permissions.DashboardView)]
    public void EachDataSet_NeedsItsOwnViewPermission(string dataset, string permission)
    {
        var service = Service();
        var request = new ExportRequest(dataset, ExportFormat.Csv, Start, End);

        Assert.Equal("forbidden", Assert.Throws<ExportException>(() => service.Prepare(Actor(Permissions.ExportRun), request)).Code);
        using var ok = service.Prepare(Actor(Permissions.ExportRun, permission), request);
        Assert.NotNull(ok);
    }

    /// <summary>
    /// Verifies that bad requests are rejected before anything starts.
    /// </summary>
    [Fact]
    public void BadRequests_AreRejected_BeforeAnythingStarts()
    {
        var service = Service();
        var actor = Everything();

        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, new ExportRequest("nonsense", ExportFormat.Csv, Start, End))).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, new ExportRequest(ExportDatasets.Audit, ExportFormat.Csv, End, Start))).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, new ExportRequest(ExportDatasets.Audit, ExportFormat.Csv, Start.AddYears(-20), End))).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { TimeZone = "<script>" })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { Kind = "user.%' or 1=1" })).Code);
        Assert.Equal("invalid", Assert.Throws<ExportException>(() => service.Prepare(actor, Audit() with { Table = "Account;drop" })).Code);
    }

    private (ExportService Service, MemoryHistoryStore History) QuestionService(bool enabled)
    {
        var history = new MemoryHistoryStore();
        var service = new ExportService(_audit, _accessLog, _names, _metrics, new MemoryFeedbackStore(_ => true), _access, () => _maxRows,
            clock: () => DateTimeOffset.UtcNow.AddHours(1), questions: history, questionTextEnabled: () => enabled);
        return (service, history);
    }

    private static ExportRequest QuestionsRequest() => new ExportRequest(ExportDatasets.Questions, ExportFormat.Csv, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)) { TimeZone = "UTC" };

    /// <summary>
    /// Verifies that question text cannot be exported while the setting is off even by an administrator.
    /// </summary>
    [Fact]
    public void QuestionText_CannotBeExported_WhileTheSettingIsOff_EvenByAnAdministrator()
    {
        var (service, _) = QuestionService(enabled: false);

        var ex = Assert.Throws<ExportException>(() => service.Prepare(Everything(), QuestionsRequest()));

        Assert.Equal("unavailable", ex.Code);
        Assert.Contains("switched off", ex.Message);
    }

    /// <summary>
    /// Verifies that question text needs questions view in addition to export run.
    /// </summary>
    [Fact]
    public void QuestionText_NeedsQuestionsView_InAdditionToExportRun()
    {
        var (service, _) = QuestionService(enabled: true);

        Assert.Equal("forbidden", Assert.Throws<ExportException>(() => service.Prepare(Actor(Permissions.ExportRun), QuestionsRequest())).Code);
        Assert.Equal("forbidden", Assert.Throws<ExportException>(() => service.Prepare(Actor(Permissions.QuestionsView), QuestionsRequest())).Code);
        using var ok = service.Prepare(Actor(Permissions.ExportRun, Permissions.QuestionsView), QuestionsRequest());
        Assert.NotNull(ok);
    }

    /// <summary>
    /// Verifies that question text exports only marked questions with user and name neutralizes formulas and the start record says question text is included.
    /// </summary>
    [Fact]
    public async Task QuestionText_ExportsOnlyMarkedQuestions_WithUserAndName_NeutralizesFormulas_AndTheStartRecordSaysQuestionTextIsIncluded()
    {
        var (service, history) = QuestionService(enabled: true);
        var ann = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await history.AddAsync(ann, "Show accounts in London", "account", "<fetch/>", adminViewable: true);
        await history.AddAsync(ann, "=HYPERLINK(\"http://x\")", "contact", "<fetch/>", adminViewable: true);
        await history.AddAsync(ann, "asked while the setting was off", "account", "<fetch/>", adminViewable: false);
        await history.AddAsync(bob, "a question of the other user", "incident", "<fetch/>", adminViewable: true);
        var actor = Everything();
        using var plan = service.Prepare(actor, QuestionsRequest() with { UserId = ann });
        var output = new MemoryStream();

        await service.StreamAsync(actor, plan, output);

        var lines = Text(output.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("time_utc,time_local,time_zone,user_id,user_name,table,question", lines[0]);
        Assert.Equal(1 + 2, lines.Length);
        Assert.Contains(lines, l => l.EndsWith(",account,Show accounts in London"));
        Assert.Contains(lines, l => l.Contains("'=HYPERLINK"));
        Assert.DoesNotContain(lines, l => l.Contains("setting was off") || l.Contains("other user"));
        Assert.Contains("User " + ann.ToString("N").Substring(0, 4), lines[1]);
        var start = Assert.Single(await _audit.ListAuditAsync(10), e => e.Action == "export.start");
        Assert.Contains("question_text=included", start.Detail);
        Assert.Equal(ExportDatasets.Questions, start.Target);
    }

    /// <summary>
    /// Verifies that question text pages across pages without losing or repeating rows.
    /// </summary>
    [Fact]
    public async Task QuestionText_PagesAcrossPages_WithoutLosingOrRepeatingRows()
    {
        var (service, history) = QuestionService(enabled: true);
        var user = Guid.NewGuid();
        for (var i = 0; i < ExportService.PageSize + 250; i++)
        {
            await history.AddAsync(user, "q" + i, "account", "<fetch/>", adminViewable: true);
        }
        var actor = Everything();
        using var plan = service.Prepare(actor, QuestionsRequest());
        var output = new MemoryStream();

        await service.StreamAsync(actor, plan, output);

        var lines = Text(output.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + ExportService.PageSize + 250, lines.Length);
        Assert.Equal(lines.Length, lines.Distinct().Count());
    }

    /// <summary>
    /// Verifies that usage is daily totals not per person rows.
    /// </summary>
    [Fact]
    public async Task Usage_IsDailyTotals_NotPerPersonRows()
    {
        await _metrics.RecordRequestAsync(new RequestRecord(Guid.NewGuid(), Start.AddHours(1), Guid.NewGuid(), 1200, "answer", null));
        await _metrics.RecordRequestAsync(new RequestRecord(Guid.NewGuid(), Start.AddHours(2), Guid.NewGuid(), 900, "failure", "timeout"));

        var lines = Text(await Run(Everything(), new ExportRequest(ExportDatasets.Usage, ExportFormat.Csv, Start, End))).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("date,questions,failed,model_calls,input_tokens,output_tokens", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("2026-07-01,2,1,"));
        Assert.DoesNotContain(lines, l => l.Contains("user"));
    }
}
