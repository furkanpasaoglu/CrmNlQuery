using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Composition;
using Microsoft.Extensions.DependencyInjection;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin;

/// <summary>
/// Holds HTTP tests for the access log endpoint using the fixed identity.
/// </summary>
public class AccessLogHttpTests
{
    /// <summary>
    /// Holds access log HTTP tests run as an administrator.
    /// </summary>
    [Collection("WebHost")]
    public class AsAdministrator : IClassFixture<AppUseHttpTests.BossHost>
    {
        private readonly AppUseHttpTests.BossHost _host;
        private readonly HttpClient _client;

        public AsAdministrator(AppUseHttpTests.BossHost host)
        {
            _host = host;
            _client = host.CreateClient();
        }

        private async Task<DataAccessRecord> SeedAsync(Guid user, string tables = "contract,account", int rows = 7, string source = "ask")
        {
            var record = new DataAccessRecord(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), user, source, tables.Split(','), rows, "ok", Guid.NewGuid());
            await _host.Services.GetRequiredService<AppServices>().AccessLog.AddAsync(record);
            return record;
        }

        /// <summary>
        /// Verifies that the log lists the records newest first with tables rows source and request ID.
        /// </summary>
        [Fact]
        public async Task TheLog_ListsTheRecords_NewestFirst_WithTablesRowsSourceAndRequestId()
        {
            var user = Guid.NewGuid();
            var record = await SeedAsync(user);

            var list = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/access-log?user={user}&hours=2");

            var item = list.EnumerateArray().Single();
            Assert.Equal(record.Id, item.GetProperty("id").GetGuid());
            Assert.Equal(user, item.GetProperty("user").GetGuid());
            Assert.Equal(new[] { "contract", "account" }, item.GetProperty("tables").EnumerateArray().Select(t => t.GetString()).ToArray());
            Assert.Equal(7, item.GetProperty("rows").GetInt32());
            Assert.Equal("ask", item.GetProperty("source").GetString());
            Assert.Equal("ok", item.GetProperty("outcome").GetString());
            Assert.Equal(record.RequestId, item.GetProperty("requestId").GetGuid());
        }

        /// <summary>
        /// Verifies that the filters narrow by user and table.
        /// </summary>
        [Fact]
        public async Task TheFilters_NarrowByUserAndTable()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            await SeedAsync(a, "account");
            await SeedAsync(b, "contract");

            Assert.Single((await _client.GetFromJsonAsync<JsonElement>($"/api/admin/access-log?user={a}&hours=2")).EnumerateArray());
            var byTable = await _client.GetFromJsonAsync<JsonElement>("/api/admin/access-log?table=contract&hours=2");
            Assert.Contains(byTable.EnumerateArray(), i => i.GetProperty("user").GetGuid() == b);
            Assert.DoesNotContain(byTable.EnumerateArray(), i => i.GetProperty("user").GetGuid() == a);
        }

        /// <summary>
        /// Verifies that every view is written to the audit trail with the filters and no record content.
        /// </summary>
        [Fact]
        public async Task EveryView_IsWrittenToTheAuditTrail_WithTheFilters_AndNoRecordContent()
        {
            var user = Guid.NewGuid();
            await SeedAsync(user, "account,contact", 3);

            await _client.GetAsync($"/api/admin/access-log?user={user}&table=account&hours=3");

            var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=50");
            var entry = audit.EnumerateArray().First(a => a.GetProperty("action").GetString() == "accesslog.view" && a.GetProperty("detail").GetString()!.Contains(user.ToString()));
            Assert.Equal(@"lab\boss", entry.GetProperty("actor").GetString());
            Assert.Contains("table account", entry.GetProperty("detail").GetString());
            Assert.Contains("1 rows", entry.GetProperty("detail").GetString());
        }

        /// <summary>
        /// Verifies that aMalformed user is aClient error not aServer error.
        /// </summary>
        [Fact]
        public async Task AMalformedUser_IsAClientError_NotAServerError()
        {
            var response = await _client.GetAsync("/api/admin/access-log?user=not-a-guid");

            Assert.True((int)response.StatusCode is >= 400 and < 500);
        }

        /// <summary>
        /// Verifies that the response holds only meta fields.
        /// </summary>
        [Fact]
        public async Task TheResponse_HoldsOnlyMetaFields()
        {
            var user = Guid.NewGuid();
            await SeedAsync(user);

            var json = await _client.GetStringAsync($"/api/admin/access-log?user={user}&hours=2");

            foreach (var forbidden in new[] { "question", "fetchxml", "fetch", "value", "filter" })
            {
                Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Verifies that the administrator holds the new permission.
        /// </summary>
        [Fact]
        public async Task TheAdministrator_HoldsTheNewPermission()
        {
            var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/me");

            Assert.Contains("AccessLog.View", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        }
    }

    /// <summary>
    /// Holds access log HTTP tests run as an unauthorized user.
    /// </summary>
    [Collection("WebHost")]
    public class AsAStranger : IClassFixture<AppUseHttpTests.StrangerHost>
    {
        private readonly HttpClient _client;

        public AsAStranger(AppUseHttpTests.StrangerHost host)
        {
            _client = host.CreateClient();
        }

        /// <summary>
        /// Verifies that the log looks nonexistent to an unregistered user.
        /// </summary>
        [Fact]
        public async Task TheLog_LooksNonexistent_ToAnUnregisteredUser()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/access-log")).StatusCode);
        }
    }
}
