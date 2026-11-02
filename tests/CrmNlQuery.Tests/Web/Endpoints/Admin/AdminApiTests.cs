using System.Net;
using CrmNlQuery.Composition;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin;

/// <summary>
/// Tests the admin panel endpoints against the real HTTP pipeline with in-memory stores and no CRM.
/// </summary>
public class AdminApiTests
{
    private const string Boss = @"LAB\boss";

    private static readonly string[] Keys =
    {
        "Identity__Mode", "Identity__FixedUser", "Identity__AllowFixedInProduction", "Admin__Bootstrap__0",
        "Llm__Provider", "Llm__AllowCloud", "Anthropic__ApiKey", "History__ConnectionString", "Identity__FixedGroups__0",
    };

    /// <summary>
    /// Test host that runs the service with a fixed administrator identity.
    /// </summary>
    public abstract class AdminHost : WebApplicationFactory<Program>
    {
        protected AdminHost(string fixedUser, string? bootstrap = null, string? fixedGroup = null)
        {
            Environment.SetEnvironmentVariable("Identity__Mode", "Fixed");
            Environment.SetEnvironmentVariable("Identity__FixedUser", fixedUser);
            Environment.SetEnvironmentVariable("Identity__AllowFixedInProduction", "false");
            Environment.SetEnvironmentVariable("Admin__Bootstrap__0", bootstrap ?? Boss);
            Environment.SetEnvironmentVariable("Identity__FixedGroups__0", fixedGroup ?? "");
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
    /// Test host that runs the service with a fixed identity of the first administrator.
    /// </summary>
    public sealed class BossHost : AdminHost { public BossHost() : base(Boss) { } }

    /// <summary>
    /// Test host with a fixed user who is the first administrator and a member of an AD group, for end-to-end group-mapping tests.
    /// </summary>
    public sealed class MemberHost : AdminHost { public MemberHost() : base(@"LAB\member", @"LAB\member", @"LAB\staff") { } }

    /// <summary>
    /// Test host with a fixed identity of an unregistered Windows account.
    /// </summary>
    public sealed class StrangerHost : AdminHost { public StrangerHost() : base(@"LAB\stranger") { } }

    /// <summary>
    /// Tests for a registered administrator calling the admin endpoints.
    /// </summary>
    [Collection("WebHost")]
    public class AsAdministrator : IClassFixture<BossHost>
    {
        private readonly HttpClient _client;

        public AsAdministrator(BossHost host)
        {
            _client = host.CreateClient();
        }

        private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 8);

        private async Task<Guid> CreateRole(string name, params string[] permissions)
        {
            var response = await _client.PostAsJsonAsync("/api/admin/roles", new { name, permissions });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        private static string Q(string account) => Uri.EscapeDataString(account);

        /// <summary>
        /// Verifies that the me endpoint shows the account, its roles and every permission.
        /// </summary>
        [Fact]
        public async Task Me_ShowsTheAccount_TheRoles_AndEveryPermission()
        {
            var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/me");

            Assert.Equal(@"lab\boss", me.GetProperty("account").GetString());
            Assert.True(me.GetProperty("isAdministrator").GetBoolean());
            Assert.Equal("Administrator", me.GetProperty("roles")[0].GetString());
            var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
            Assert.Contains("Users.Manage", permissions);
            Assert.Contains("Dashboard.View", permissions);
            Assert.Contains("Metadata.Refresh", permissions);
            Assert.Equal("index.html", me.GetProperty("appHome").GetString());
        }

        private static string K(string key) => Uri.EscapeDataString(key);

        private async Task<JsonElement> Setting(string key)
        {
            var all = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
            return all.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray())
                .Single(i => i.GetProperty("key").GetString() == key);
        }

        /// <summary>
        /// Verifies that the report exposes the blocked requests and knowledge usage fields.
        /// </summary>
        [Fact]
        public async Task TheReport_ExposesTheBlockedRequestsAndKnowledgeUsageFields()
        {
            var report = await _client.GetFromJsonAsync<JsonElement>("/api/admin/report?days=7");

            Assert.True(report.TryGetProperty("blocked", out var blocked));
            Assert.Equal(JsonValueKind.Number, blocked.ValueKind);
            Assert.Equal(JsonValueKind.Array, report.GetProperty("blockedByType").ValueKind);
            Assert.True(report.TryGetProperty("knowledgeSearched", out _));
            Assert.True(report.TryGetProperty("knowledgeUsed", out _));
            Assert.True(report.TryGetProperty("knowledgeUsedRate", out _));
            Assert.True(report.TryGetProperty("knowledgeAveragePieces", out _));
            Assert.True(report.TryGetProperty("followUps", out _));
            Assert.True(report.TryGetProperty("followUpRate", out _));
        }

        /// <summary>
        /// Verifies that settings are listed in groups with their source default and range.
        /// </summary>
        [Fact]
        public async Task Settings_AreListedInGroups_WithTheirSourceDefaultAndRange()
        {
            var all = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");

            Assert.True(all.GetProperty("askEnabled").GetBoolean());
            var groups = all.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()).ToList();
            Assert.Equal(new[] { "Capacity", "Content", "Knowledge", "Planning", "Conversation", "Retention", "Features", "Interface", "Access" }, groups);
            var perMinute = await Setting("Limits:QuestionsPerMinute");
            Assert.Equal("20", perMinute.GetProperty("value").GetString());
            Assert.Equal("default", perMinute.GetProperty("source").GetString());
            Assert.Equal("20", perMinute.GetProperty("default").GetString());
            Assert.Equal(600, perMinute.GetProperty("max").GetInt32());
            Assert.Equal("integer", perMinute.GetProperty("kind").GetString());
        }

        /// <summary>
        /// Verifies that a setting can be changed, shows as panel-sourced, is audited and can be reset.
        /// </summary>
        [Fact]
        public async Task ASettingCanBeChanged_ShowsAsPanel_IsAudited_AndCanBeReset()
        {
            var put = await _client.PutAsJsonAsync($"/api/admin/settings?key={K("Limits:QuestionsPerDay")}", new { value = "123" });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var changed = await Setting("Limits:QuestionsPerDay");
            Assert.Equal("123", changed.GetProperty("value").GetString());
            Assert.Equal("panel", changed.GetProperty("source").GetString());
            var audit = await _client.GetStringAsync("/api/admin/audit?take=50");
            Assert.Contains("setting.set", audit);
            Assert.Contains("500 -> 123", audit);

            var reset = await _client.DeleteAsync($"/api/admin/settings?key={K("Limits:QuestionsPerDay")}");
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            Assert.Equal("default", (await Setting("Limits:QuestionsPerDay")).GetProperty("source").GetString());
        }

        /// <summary>
        /// Verifies that an invalid setting value is refused with a message and nothing changes.
        /// </summary>
        [Theory]
        [InlineData("Limits:QuestionsPerMinute", "100000")]
        [InlineData("Limits:QuestionsPerMinute", "abc")]
        [InlineData("Limits:AskEnabled", "maybe")]
        [InlineData("Knowledge:MinSimilarity", "2")]
        public async Task AnInvalidValue_IsRefused_WithAMessage_AndNothingChanges(string key, string value)
        {
            var response = await _client.PutAsJsonAsync($"/api/admin/settings?key={K(key)}", new { value });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
            Assert.Equal("default", (await Setting(key)).GetProperty("source").GetString());
        }

        /// <summary>
        /// Verifies that the scheduled quality run account cannot be set through the generic settings endpoint.
        /// </summary>
        [Fact]
        public async Task TheScheduledRunAccount_CannotBeSetThroughTheGenericSettingsEndpoint()
        {
            var put = await _client.PutAsJsonAsync($"/api/admin/settings?key={K("Quality:ScheduleRunAs")}", new { value = @"LAB\someone.else" });
            var delete = await _client.DeleteAsync($"/api/admin/settings?key={K("Quality:ScheduleRunAs")}");
            var all = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
            var keys = all.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("key").GetString()).ToList();

            Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
            Assert.DoesNotContain("Quality:ScheduleRunAs", keys);
            Assert.Contains("Quality:ScheduleEnabled", keys);
        }

        /// <summary>
        /// Verifies that settings outside the allow list cannot be changed or reset even by an administrator.
        /// </summary>
        [Theory]
        [InlineData("Anthropic:ApiKey")]
        [InlineData("Identity:Mode")]
        [InlineData("Llm:AllowCloud")]
        [InlineData("History:ConnectionString")]
        [InlineData("Admin:Bootstrap")]
        [InlineData("Cors:AllowedOrigins")]
        [InlineData("Nothing:Here")]
        public async Task SettingsOutsideTheAllowList_CannotBeChangedOrReset_EvenByAnAdministrator(string key)
        {
            var put = await _client.PutAsJsonAsync($"/api/admin/settings?key={K(key)}", new { value = "x" });
            var delete = await _client.DeleteAsync($"/api/admin/settings?key={K(key)}");

            Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        }

        /// <summary>
        /// Verifies that pausing asking shows in the settings list immediately and resuming restores it.
        /// </summary>
        [Fact]
        public async Task PausingAsking_ShowsInTheListImmediately_AndResumingRestoresIt()
        {
            await _client.PutAsJsonAsync($"/api/admin/settings?key={K("Limits:AskEnabled")}", new { value = "false" });
            try
            {
                var paused = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
                Assert.False(paused.GetProperty("askEnabled").GetBoolean());
                Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/admin/me")).StatusCode);
            }
            finally
            {
                await _client.DeleteAsync($"/api/admin/settings?key={K("Limits:AskEnabled")}");
            }
            Assert.True((await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings")).GetProperty("askEnabled").GetBoolean());
        }

        /// <summary>
        /// Verifies that documents start empty and the documents permission is offered.
        /// </summary>
        [Fact]
        public async Task Documents_StartEmpty_AndTheDocumentsPermissionIsOffered()
        {
            var list = await _client.GetFromJsonAsync<JsonElement>("/api/admin/documents");

            Assert.Equal(0, list.GetProperty("documents").GetArrayLength());
            Assert.False(list.GetProperty("searchEnabled").GetBoolean());
            var permissions = await _client.GetFromJsonAsync<JsonElement>("/api/admin/permissions");
            Assert.Contains(permissions.EnumerateArray(), p => p.GetProperty("key").GetString() == "Documents.Manage");
        }

        /// <summary>
        /// Verifies that creating a document without a title or text is refused with a message.
        /// </summary>
        [Theory]
        [InlineData("", "text")]
        [InlineData("Title", "  ")]
        public async Task CreatingADocument_WithoutATitleOrText_IsRefused_WithAMessage(string title, string body)
        {
            var response = await _client.PostAsJsonAsync("/api/admin/documents", new { title, body });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
        }

        /// <summary>
        /// Verifies that a missing document returns 404 on read, update, toggle and delete.
        /// </summary>
        [Fact]
        public async Task AMissingDocument_Is404_OnReadUpdateToggleAndDelete()
        {
            var id = Guid.NewGuid();

            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/admin/documents/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/api/admin/documents/{id}", new { title = "t", body = "b" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/api/admin/documents/{id}/enabled", new { enabled = false })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/admin/documents/{id}")).StatusCode);
        }

        /// <summary>
        /// Verifies that a blank search returns nothing without needing the embedding model.
        /// </summary>
        [Fact]
        public async Task ABlankSearch_ReturnsNothing_WithoutNeedingTheEmbeddingModel()
        {
            var response = await _client.PostAsJsonAsync("/api/admin/documents/search", new { question = " " });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        }

        /// <summary>
        /// Verifies that a document can be added, found, disabled and deleted through the API (needs a real embedding model).
        /// </summary>
        [Fact]
        [Trait("Category", "Live")]
        public async Task ADocument_CanBeAdded_Found_Disabled_AndDeleted_ThroughTheApi()
        {
            var marker = "zq" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var create = await _client.PostAsJsonAsync("/api/admin/documents",
                new { title = "KOBİ segmenti " + marker, body = "KOBİ müşteriler için new_musteriSegmenti " + marker + " alanı kullanılır, değer 2. Gizli iş kuralı metni." });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            try
            {
                var hits = await (await _client.PostAsJsonAsync("/api/admin/documents/search", new { question = "küçük işletme müşterileri hangi alan " + marker })).Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("documentId").GetGuid() == id);

                Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/admin/documents/{id}/enabled", new { enabled = false })).StatusCode);
                var after = await (await _client.PostAsJsonAsync("/api/admin/documents/search", new { question = "küçük işletme müşterileri hangi alan " + marker })).Content.ReadFromJsonAsync<JsonElement>();
                Assert.DoesNotContain(after.EnumerateArray(), h => h.GetProperty("documentId").GetGuid() == id);

                var audit = await _client.GetStringAsync("/api/admin/audit?take=50");
                Assert.Contains("doc.create", audit);
                Assert.DoesNotContain("Gizli iş kuralı", audit);
            }
            finally
            {
                Assert.Equal(HttpStatusCode.OK, (await _client.DeleteAsync($"/api/admin/documents/{id}")).StatusCode);
            }
        }

        /// <summary>
        /// Verifies that a metadata refresh is audited even when the CRM is unreachable, without the error text.
        /// </summary>
        [Fact]
        public async Task AMetadataRefresh_IsAudited_AlsoWhenTheCrmIsUnreachable_WithoutTheErrorText()
        {
            var response = await _client.PostAsync("/api/admin/metadata/refresh", null);

            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
            var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=200");
            var refresh = audit.EnumerateArray().Where(a => a.GetProperty("action").GetString() == "metadata.refresh").ToList();
            Assert.NotEmpty(refresh);
            Assert.Equal(@"lab\boss", refresh[0].GetProperty("actor").GetString());
            Assert.Contains(refresh[0].GetProperty("detail").GetString(), new[] { "ok", "failed" });
        }

        /// <summary>
        /// Verifies that the old unauthenticated metadata refresh route no longer exists.
        /// </summary>
        [Fact]
        public async Task TheOldUnauthenticatedMetadataRefreshRoute_IsGone()
        {
            var response = await _client.PostAsync("/api/metadata/refresh", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>
        /// Verifies that the report is served to an administrator without any CRM.
        /// </summary>
        [Fact]
        public async Task TheReport_IsServed_ToAnAdministrator_WithoutAnyCrm()
        {
            var response = await _client.GetAsync("/api/admin/report?days=7");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// Verifies that a logged warning shows up in the logs within seconds and the filters apply.
        /// </summary>
        [Fact]
        public async Task ALoggedWarning_ShowsUpInTheLogs_WithinAFewSeconds_AndTheFiltersApply()
        {
            var marker = "e2e" + Guid.NewGuid().ToString("N");
            Serilog.Log.Warning("Request {RequestId} ended with {Kind} " + marker, "req-" + marker, "crm_error");

            JsonElement found = default;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var list = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/logs?q={marker}&hours=1");
                if (list.GetArrayLength() > 0) { found = list[0]; break; }
                await Task.Delay(250);
            }

            Assert.Equal(JsonValueKind.Object, found.ValueKind);
            Assert.Equal("Warning", found.GetProperty("level").GetString());
            Assert.Equal("req-" + marker, found.GetProperty("requestId").GetString());
            var errorsOnly = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/logs?q={marker}&level=Error&hours=1");
            Assert.Equal(0, errorsOnly.GetArrayLength());
            var byRequestId = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/logs?q=req-{marker}&hours=1");
            Assert.Equal(1, byRequestId.GetArrayLength());
        }

        /// <summary>
        /// Verifies that the permissions endpoint lists every permission defined in code.
        /// </summary>
        [Fact]
        public async Task Permissions_ListsEveryPermissionDefinedInCode()
        {
            var list = await _client.GetFromJsonAsync<JsonElement>("/api/admin/permissions");

            Assert.Contains(list.EnumerateArray(), p => p.GetProperty("key").GetString() == "Audit.View");
            Assert.True(list.GetArrayLength() >= 6);
        }

        /// <summary>
        /// Verifies that the administrator role is listed with all permissions as built in.
        /// </summary>
        [Fact]
        public async Task TheAdministratorRole_IsListedWithAllPermissions_AsBuiltIn()
        {
            var roles = await _client.GetFromJsonAsync<JsonElement>("/api/admin/roles");

            var admin = roles.EnumerateArray().Single(r => r.GetProperty("builtin").GetBoolean());
            Assert.Equal("Administrator", admin.GetProperty("name").GetString());
            Assert.True(admin.GetProperty("permissions").GetArrayLength() >= 6);
        }

        /// <summary>
        /// Verifies that a role can be created listed updated and deleted and each step is audited.
        /// </summary>
        [Fact]
        public async Task ARole_CanBeCreated_Listed_Updated_AndDeleted_AndEachStepIsAudited()
        {
            var name = Unique("Viewer-");
            var id = await CreateRole(name, "Dashboard.View");

            var listed = await _client.GetFromJsonAsync<JsonElement>("/api/admin/roles");
            Assert.Contains(listed.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);

            var update = await _client.PutAsJsonAsync($"/api/admin/roles/{id}", new { name, permissions = new[] { "Dashboard.View", "Logs.View" } });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var delete = await _client.DeleteAsync($"/api/admin/roles/{id}");
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

            var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=200");
            var actions = audit.EnumerateArray().Where(a => a.GetProperty("target").GetString() == name).Select(a => a.GetProperty("action").GetString()).ToList();
            Assert.Equal(new[] { "role.delete", "role.update", "role.create" }, actions);
        }

        /// <summary>
        /// Verifies that a user can be added with a role deactivated re roled and deleted.
        /// </summary>
        [Fact]
        public async Task AUser_CanBeAdded_WithARole_Deactivated_ReRoled_AndDeleted()
        {
            var roleId = await CreateRole(Unique("Ops-"), "Logs.View");
            var account = @"LAB\" + Unique("worker");

            var add = await _client.PostAsJsonAsync("/api/admin/users", new { account, displayName = "Worker", roleIds = new[] { roleId } });
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);

            var users = await _client.GetFromJsonAsync<JsonElement>("/api/admin/users");
            var added = users.EnumerateArray().Single(u => u.GetProperty("account").GetString() == account.ToLowerInvariant());
            Assert.Equal("Worker", added.GetProperty("displayName").GetString());
            Assert.True(added.GetProperty("active").GetBoolean());
            Assert.Equal(1, added.GetProperty("roles").GetArrayLength());

            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/admin/users/active?account={Q(account)}", new { active = false })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/admin/users/roles?account={Q(account)}", new { roleIds = new Guid[0] })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.DeleteAsync($"/api/admin/users?account={Q(account)}")).StatusCode);

            var after = await _client.GetFromJsonAsync<JsonElement>("/api/admin/users");
            Assert.DoesNotContain(after.EnumerateArray(), u => u.GetProperty("account").GetString() == account.ToLowerInvariant());
        }

        /// <summary>
        /// Verifies that rule violations are translated to HTTP statuses with an error message.
        /// </summary>
        [Fact]
        public async Task RuleViolations_AreTranslatedToHttpStatuses_WithAnErrorMessage()
        {
            var name = Unique("Dup-");
            await CreateRole(name, "Logs.View");

            var unknown = await _client.PostAsJsonAsync("/api/admin/roles", new { name = Unique("Odd-"), permissions = new[] { "Nothing.Here" } });
            var duplicate = await _client.PostAsJsonAsync("/api/admin/roles", new { name = name.ToUpperInvariant(), permissions = new[] { "Logs.View" } });
            var lastAdmin = await _client.PutAsJsonAsync($"/api/admin/users/active?account={Q(Boss)}", new { active = false });
            var builtIn = await _client.PutAsJsonAsync("/api/admin/roles/00000000-0000-0000-0000-0000000000a1", new { name = "Boss", permissions = new string[0] });
            var ghost = await _client.DeleteAsync($"/api/admin/users?account={Q(@"LAB\ghost")}");
            var badAccount = await _client.PostAsJsonAsync("/api/admin/users", new { account = "noDomain", roleIds = new Guid[0] });

            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, lastAdmin.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, builtIn.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, badAccount.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await lastAdmin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
        }

        /// <summary>
        /// Verifies that admin endpoints refuse writes from another site even for an administrator.
        /// </summary>
        [Fact]
        public async Task AdminEndpoints_RefuseWrites_FromAnotherSite_EvenForAnAdministrator()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/roles")
            {
                Content = JsonContent.Create(new { name = Unique("Evil-"), permissions = new[] { "Logs.View" } }),
            };
            request.Headers.Add("Sec-Fetch-Site", "cross-site");

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>
    /// Tests for a user who gets access through an AD group mapping.
    /// </summary>
    [Collection("WebHost")]
    public class AsAGroupMember : IClassFixture<MemberHost>
    {
        private readonly HttpClient _client;

        public AsAGroupMember(MemberHost host)
        {
            _client = host.CreateClient();
        }

        /// <summary>
        /// Verifies that after giving up direct roles the group mapping still lets the user in with only the group's permissions.
        /// </summary>
        [Fact]
        public async Task AfterGivingUpTheDirectRoles_TheGroupMappingStillLetsTheUserIn_WithJustTheGroupsPermissions()
        {
            var role = await _client.PostAsJsonAsync("/api/admin/roles", new { name = "Logs only", permissions = new[] { "Logs.View" } });
            var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/admin/users", new { account = @"LAB\other", roleIds = new[] { Guid.Parse("00000000-0000-0000-0000-0000000000a1") } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/admin/groups/roles?group={Uri.EscapeDataString(@"LAB\staff")}", new { roleIds = new[] { roleId } })).StatusCode);
            var groups = await _client.GetFromJsonAsync<JsonElement>("/api/admin/groups");
            Assert.Equal(@"LAB\staff", groups[0].GetProperty("group").GetString());
            Assert.Equal("Logs only", groups[0].GetProperty("roles")[0].GetString());

            var demote = await _client.PutAsJsonAsync($"/api/admin/users/roles?account={Uri.EscapeDataString(@"LAB\member")}", new { roleIds = new Guid[0] });
            Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

            var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/me");
            Assert.False(me.GetProperty("isAdministrator").GetBoolean());
            Assert.Equal("Logs only", me.GetProperty("roles")[0].GetString());
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/admin/logs?hours=1")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/groups")).StatusCode);
        }
    }

    /// <summary>
    /// Tests for a Windows account that is not registered in the tool.
    /// </summary>
    [Collection("WebHost")]
    public class AsAStranger : IClassFixture<StrangerHost>
    {
        private readonly HttpClient _client;

        private readonly StrangerHost _host;

        public AsAStranger(StrangerHost host)
        {
            _host = host;
            _client = host.CreateClient();
        }

        /// <summary>
        /// Verifies that a denied admin attempt leaves an audit record without the query string.
        /// </summary>
        [Fact]
        public async Task ADeniedAdminAttempt_LeavesAnAuditRecord_WithoutTheQueryString()
        {
            var response = await _client.PutAsJsonAsync("/api/admin/settings?key=Limits:AskEnabled", new { value = "false" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var audit = await _host.Services.GetRequiredService<AppServices>().Access.ListAuditAsync(200);
            var entry = audit.First(e => e.Action == "access.denied" && e.Actor == @"lab\stranger" && e.Target == "PUT /api/admin/settings");
            Assert.Equal("PUT /api/admin/settings", entry.Target);
            Assert.Equal("needs Settings.Manage; no_access", entry.Detail);
            Assert.DoesNotContain("AskEnabled", entry.Target + entry.Detail);
        }

        /// <summary>
        /// Verifies that every admin endpoint looks nonexistent to an unregistered user.
        /// </summary>
        [Theory]
        [InlineData("GET", "/api/admin/me")]
        [InlineData("GET", "/api/admin/report")]
        [InlineData("GET", "/api/admin/health")]
        [InlineData("GET", "/api/admin/users")]
        [InlineData("GET", "/api/admin/roles")]
        [InlineData("GET", "/api/admin/permissions")]
        [InlineData("GET", "/api/admin/audit")]
        [InlineData("GET", "/api/admin/logs")]
        [InlineData("GET", "/api/admin/groups")]
        [InlineData("GET", "/api/admin/settings")]
        [InlineData("PUT", "/api/admin/settings?key=Limits:AskEnabled")]
        [InlineData("DELETE", "/api/admin/settings?key=Limits:AskEnabled")]
        [InlineData("GET", "/api/admin/documents")]
        [InlineData("GET", "/api/admin/documents/00000000-0000-0000-0000-000000000001")]
        [InlineData("POST", "/api/admin/documents")]
        [InlineData("POST", "/api/admin/documents/search")]
        [InlineData("PUT", "/api/admin/documents/00000000-0000-0000-0000-000000000001")]
        [InlineData("DELETE", "/api/admin/documents/00000000-0000-0000-0000-000000000001")]
        [InlineData("POST", "/api/admin/metadata/refresh")]
        [InlineData("POST", "/api/admin/roles")]
        [InlineData("POST", "/api/admin/users")]
        public async Task EveryAdminEndpoint_LooksNonexistent_ToAnUnregisteredUser(string method, string path)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "POST" || method == "PUT")
            {
                request.Content = JsonContent.Create(new { name = "x", permissions = new string[0], account = @"LAB\x", title = "t", body = "b", question = "q" });
            }

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
