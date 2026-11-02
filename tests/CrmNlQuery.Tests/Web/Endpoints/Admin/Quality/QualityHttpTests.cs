using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin.Quality;

/// <summary>
/// Tests the quality check endpoints through the real HTTP pipeline: permissions, disabled feature and empty states.
/// </summary>
public class QualityHttpTests
{
    /// <summary>
    /// Tests the quality endpoints as an administrator.
    /// </summary>
    [Collection("WebHost")]
    public class AsAdministrator : IClassFixture<AppUseHttpTests.BossHost>
    {
        private readonly HttpClient _client;

        public AsAdministrator(AppUseHttpTests.BossHost host)
        {
            _client = host.CreateClient();
        }

        /// <summary>
        /// Verifies that the case list is empty at first and says it is not persistent.
        /// </summary>
        [Fact]
        public async Task TheCaseList_IsEmptyAtFirst_AndSaysItIsNotPersistent()
        {
            var response = await _client.GetAsync("/api/admin/quality/cases");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(json.GetProperty("persistent").GetBoolean());
            Assert.Equal(0, json.GetProperty("cases").GetArrayLength());
            Assert.Equal(500, json.GetProperty("maxCases").GetInt32());
        }

        /// <summary>
        /// Verifies that actions without ids change nothing.
        /// </summary>
        [Fact]
        public async Task TheActionsWithoutIds_ChangeNothing()
        {
            foreach (var path in new[] { "approve", "retire", "restore", "delete" })
            {
                var response = await _client.PostAsJsonAsync("/api/admin/quality/" + path, new { ids = new string[0] });

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetInt32());
            }
        }

        /// <summary>
        /// Verifies that the export is a download with no caching and an empty array.
        /// </summary>
        [Fact]
        public async Task TheExport_IsADownload_WithNoCaching_AndAnEmptyArray()
        {
            var response = await _client.GetAsync("/api/admin/quality/export");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? "");
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("[]", (await response.Content.ReadAsStringAsync()).Trim());
        }

        /// <summary>
        /// Verifies that generating without a table is refused.
        /// </summary>
        [Fact]
        public async Task GeneratingWithoutATable_IsRefused()
        {
            var response = await _client.PostAsJsonAsync("/api/admin/quality/generate", new { entity = "" });

            Assert.True((int)response.StatusCode is 400 or 401 or 503, "got " + (int)response.StatusCode);
        }

        /// <summary>
        /// Verifies that the feature switch hides the endpoints.
        /// </summary>
        [Fact]
        public async Task TheFeatureSwitch_Hides_TheEndpoints()
        {
            var off = await _client.PutAsJsonAsync("/api/admin/settings?key=Features:Quality", new { value = "false" });
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            try
            {
                Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/quality/cases")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/quality/export")).StatusCode);
            }
            finally
            {
                await _client.PutAsJsonAsync("/api/admin/settings?key=Features:Quality", new { value = "true" });
            }
        }

        /// <summary>
        /// Verifies that the run list is empty at first and unknown runs are not found.
        /// </summary>
        [Fact]
        public async Task TheRunList_IsEmptyAtFirst_AndUnknownRunsAreNotFound()
        {
            var list = await _client.GetFromJsonAsync<JsonElement>("/api/admin/quality/runs");

            Assert.Equal(0, list.GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/quality/runs/" + Guid.NewGuid().ToString("N"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/admin/quality/runs/" + Guid.NewGuid().ToString("N") + "/cancel", null)).StatusCode);
        }

        /// <summary>
        /// Verifies that starting a run needs a known CRM user.
        /// </summary>
        [Fact]
        public async Task StartingARun_NeedsAKnownCrmUser()
        {
            var response = await _client.PostAsJsonAsync("/api/admin/quality/runs", new { repeat = 1 });

            Assert.True((int)response.StatusCode is 401 or 503, "got " + (int)response.StatusCode);
        }

        /// <summary>
        /// Verifies that the new permission is listed for roles.
        /// </summary>
        [Fact]
        public async Task TheNewPermission_IsListedForRoles()
        {
            var permissions = await _client.GetFromJsonAsync<JsonElement>("/api/admin/permissions");

            Assert.Contains(permissions.EnumerateArray(), p => p.GetProperty("key").GetString() == "Quality.Manage");
        }
    }

    /// <summary>
    /// Tests the quality endpoints as a user without permission.
    /// </summary>
    [Collection("WebHost")]
    public class AsSomeoneWithoutPermission : IClassFixture<AppUseHttpTests.StrangerHost>
    {
        private readonly HttpClient _client;

        public AsSomeoneWithoutPermission(AppUseHttpTests.StrangerHost host)
        {
            _client = host.CreateClient();
        }

        /// <summary>
        /// Verifies that every endpoint is hidden.
        /// </summary>
        [Fact]
        public async Task EveryEndpoint_IsHidden()
        {
            foreach (var path in new[] { "/api/admin/quality/cases", "/api/admin/quality/entities", "/api/admin/quality/export", "/api/admin/quality/runs", "/api/admin/quality/runs/abc" })
            {
                var status = (await _client.GetAsync(path)).StatusCode;
                Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, path + " answered " + (int)status);
            }
            var post = await _client.PostAsJsonAsync("/api/admin/quality/delete", new { ids = new[] { "x" } });
            Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
        }
    }
}
