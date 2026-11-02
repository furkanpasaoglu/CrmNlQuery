using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin;

/// <summary>
/// Tests the App.Use requirement and diagnostic endpoint against the real HTTP pipeline, with the gate running before CRM user resolution.
/// </summary>
public class AppUseHttpTests
{
    internal const string Boss = @"LAB\boss";

    private static readonly string[] Keys =
    {
        "Identity__Mode", "Identity__FixedUser", "Identity__AllowFixedInProduction", "Admin__Bootstrap__0",
        "Llm__Provider", "Llm__AllowCloud", "Anthropic__ApiKey", "History__ConnectionString", "Identity__FixedGroups__0", "Identity__FixedGroups__1",
    };

    /// <summary>
    /// Abstract test host that runs the web app with a fixed identity and fixed groups.
    /// </summary>
    public abstract class FixedHost : WebApplicationFactory<Program>
    {
        protected FixedHost(string user)
        {
            Environment.SetEnvironmentVariable("Identity__Mode", "Fixed");
            Environment.SetEnvironmentVariable("Identity__FixedUser", user);
            Environment.SetEnvironmentVariable("Identity__AllowFixedInProduction", "false");
            Environment.SetEnvironmentVariable("Admin__Bootstrap__0", Boss);
            Environment.SetEnvironmentVariable("Identity__FixedGroups__0", @"LAB\Sales");
            Environment.SetEnvironmentVariable("Identity__FixedGroups__1", @"LAB\Everyone");
            Environment.SetEnvironmentVariable("Llm__Provider", "Ollama");
            Environment.SetEnvironmentVariable("Llm__AllowCloud", "false");
            Environment.SetEnvironmentVariable("Anthropic__ApiKey", "");
            Environment.SetEnvironmentVariable("History__ConnectionString", "");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Crm:BaseUrl", "http://crm.example/org");
            builder.UseSetting("Setup:Completed", "true");
            builder.UseSetting("Logging:File:Path", Path.Combine(Path.GetTempPath(), "nlquery-tests", "log-.txt"));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            foreach (var key in Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    /// <summary>
    /// Test host whose fixed identity is the bootstrap administrator.
    /// </summary>
    public sealed class BossHost : FixedHost { public BossHost() : base(Boss) { } }

    /// <summary>
    /// Test host whose fixed identity is an unregistered stranger.
    /// </summary>
    public sealed class StrangerHost : FixedHost { public StrangerHost() : base(@"LAB\stranger") { } }

    /// <summary>
    /// Tests App.Use behavior as an administrator: setting, diagnostic endpoint and the panel staying unaffected.
    /// </summary>
    [Collection("WebHost")]
    public class AsAdministrator : IClassFixture<BossHost>
    {
        private readonly HttpClient _client;

        public AsAdministrator(BossHost host)
        {
            _client = host.CreateClient();
        }

        private async Task SetModeAsync(string value)
        {
            var response = await _client.PutAsJsonAsync("/api/admin/settings?key=Access:RequireAppUse", new { value });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// Verifies that the setting is listed under access as a choice defaulting to off.
        /// </summary>
        [Fact]
        public async Task TheSetting_IsListedUnderAccess_AsAChoice_DefaultingToOff()
        {
            var settings = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
            var access = settings.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "Access");
            var item = access.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "Access:RequireAppUse");

            Assert.Equal("Access:RequireAppUse", item.GetProperty("key").GetString());
            Assert.Equal("choice", item.GetProperty("kind").GetString());
            Assert.Equal("off", item.GetProperty("default").GetString());
            Assert.Equal(new[] { "off", "report", "on" }, item.GetProperty("choices").EnumerateArray().Select(c => c.GetString()).ToArray());
        }

        /// <summary>
        /// Verifies that an invalid value is refused and the mode stays off.
        /// </summary>
        [Fact]
        public async Task AnInvalidValue_IsRefused_AndTheModeStaysOff()
        {
            var response = await _client.PutAsJsonAsync("/api/admin/settings?key=Access:RequireAppUse", new { value = "maybe" });

            Assert.True((int)response.StatusCode is >= 400 and < 500);
            var settings = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
            var item = settings.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "Access").GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "Access:RequireAppUse");
            Assert.Equal("off", item.GetProperty("value").GetString());
        }

        /// <summary>
        /// Verifies that the admin panel is never affected by the setting even when it is on.
        /// </summary>
        [Fact]
        public async Task TheAdminPanel_IsNeverAffected_ByTheSetting_EvenWhenItIsOn()
        {
            await SetModeAsync("on");
            try
            {
                Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/admin/me")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/admin/settings")).StatusCode);
            }
            finally
            {
                await SetModeAsync("off");
            }
        }

        /// <summary>
        /// Verifies that the identity endpoint shows the caller's own account, groups, roles and the mode.
        /// </summary>
        [Fact]
        public async Task TheIdentityEndpoint_ShowsTheCallersOwnAccount_Groups_RolesAndTheMode()
        {
            var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/identity");

            Assert.Equal(@"lab\boss", me.GetProperty("account").GetString());
            Assert.Equal("Fixed", me.GetProperty("identityMode").GetString());
            Assert.Equal(new[] { @"LAB\Everyone", @"LAB\Sales" }, me.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).ToArray());
            Assert.Contains("Administrator", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            Assert.True(me.GetProperty("hasAppUse").GetBoolean());
            Assert.Equal("off", me.GetProperty("appUseMode").GetString());
            Assert.True(me.GetProperty("isAdministrator").GetBoolean());
        }

        /// <summary>
        /// Verifies that the identity endpoint lists the mapped groups the user is a member of.
        /// </summary>
        [Fact]
        public async Task TheIdentityEndpoint_ListsTheMappedGroupsTheUserIsAMemberOf()
        {
            var roles = await _client.GetFromJsonAsync<JsonElement>("/api/admin/roles");
            var roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid();
            var put = await _client.PutAsJsonAsync("/api/admin/groups/roles?group=" + Uri.EscapeDataString(@"LAB\Sales") + "&confirm=true", new { roleIds = new[] { roleId } });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            try
            {
                var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/identity");

                Assert.Equal(new[] { @"LAB\Sales" }, me.GetProperty("memberOfMappedGroups").EnumerateArray().Select(g => g.GetString()).ToArray());
            }
            finally
            {
                await _client.DeleteAsync("/api/admin/groups?group=" + Uri.EscapeDataString(@"LAB\Sales"));
            }
        }
    }

    /// <summary>
    /// Tests App.Use behavior as an unregistered user, exercising the gate itself.
    /// </summary>
    [Collection("WebHost")]
    public class AsAStranger : IClassFixture<StrangerHost>
    {
        private readonly StrangerHost _host;
        private readonly HttpClient _client;

        public AsAStranger(StrangerHost host)
        {
            _host = host;
            _client = host.CreateClient();
        }

        private async Task SetModeAsync(string value)
        {
            var result = await _host.Services.GetRequiredService<AppServices>().Settings.SetAsync("test", "Access:RequireAppUse", value);
            Assert.True(result.Ok, result.Message);
        }

        private async Task<IReadOnlyList<CrmNlQuery.Domain.Access.Entities.AuditEntry>> AuditAsync(string action)
            => (await _host.Services.GetRequiredService<AppServices>().Access.ListAuditAsync(200)).Where(e => e.Action == action && e.Actor == @"lab\stranger").ToList();

        private Task<HttpResponseMessage> AskAsync() => _client.PostAsJsonAsync("/api/ask", new { question = "x" });

        /// <summary>
        /// Verifies that the identity endpoint looks nonexistent to an unregistered user.
        /// </summary>
        [Fact]
        public async Task TheIdentityEndpoint_LooksNonexistent_ToAnUnregisteredUser()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/identity")).StatusCode);
        }

        /// <summary>
        /// Verifies that when off the gate does not run and the request proceeds to the CRM identity step.
        /// </summary>
        [Fact]
        public async Task Off_TheGateDoesNotRun_TheRequestGoesOnToTheCrmIdentityStep()
        {
            await SetModeAsync("off");
            var deniedBefore = (await AuditAsync("access.denied")).Count;
            var wouldBefore = (await AuditAsync("access.would_deny")).Count;

            var response = await AskAsync();

            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(deniedBefore, (await AuditAsync("access.denied")).Count);
            Assert.Equal(wouldBefore, (await AuditAsync("access.would_deny")).Count);
        }

        /// <summary>
        /// Verifies that when on an unregistered user gets a 403 with a plain message and the attempt is audited with their groups.
        /// </summary>
        [Fact]
        public async Task On_AnUnregisteredUserGetsA403_WithAPlainMessage_AndTheAttemptIsAudited_WithTheirGroups()
        {
            await SetModeAsync("on");
            try
            {
                var response = await AskAsync();

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("failure", body.GetProperty("kind").GetString());
                Assert.Contains("not allowed", body.GetProperty("message").GetString());
                Assert.DoesNotContain("LAB", body.GetProperty("message").GetString());
                var entry = (await AuditAsync("access.denied")).First(e => e.Target == "POST /api/ask");
                Assert.Equal(@"needs App.Use; no_access; groups: LAB\Everyone, LAB\Sales", entry.Detail);
            }
            finally
            {
                await SetModeAsync("off");
            }
        }

        /// <summary>
        /// Verifies that when on every user endpoint is gated, not only ask.
        /// </summary>
        [Fact]
        public async Task On_EveryUserEndpointIsGated_NotOnlyAsk()
        {
            await SetModeAsync("on");
            try
            {
                foreach (var path in new[] { "/api/chats", "/api/chats/counts", "/api/me/preferences" })
                {
                    Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(path)).StatusCode);
                }
            }
            finally
            {
                await SetModeAsync("off");
            }
        }

        /// <summary>
        /// Verifies that report mode does not block but writes a would-deny record with the user's groups.
        /// </summary>
        [Fact]
        public async Task Report_DoesNotBlock_ButWritesAWouldDenyRecord_WithTheirGroups()
        {
            await SetModeAsync("report");
            try
            {
                var response = await _client.GetAsync("/api/chats/counts");

                Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
                var entry = (await AuditAsync("access.would_deny")).First(e => e.Target == "GET /api/chats/counts");
                Assert.Equal(@"needs App.Use; no_access; groups: LAB\Everyone, LAB\Sales", entry.Detail);
                Assert.DoesNotContain(await AuditAsync("access.denied"), e => e.Target == "GET /api/chats/counts");
            }
            finally
            {
                await SetModeAsync("off");
            }
        }
    }
}
