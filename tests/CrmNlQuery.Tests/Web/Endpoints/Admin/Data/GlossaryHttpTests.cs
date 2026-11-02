using System.Net;
using System.Net.Http.Json;
using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin.Data;

/// <summary>
/// Tests the business glossary endpoints against the real HTTP pipeline; without permission they look nonexistent (404).
/// </summary>
public class GlossaryHttpTests
{
    private static readonly (string Method, string Path)[] Endpoints =
    {
        ("GET", "/api/admin/glossary"),
        ("GET", "/api/admin/glossary/summary"),
        ("GET", "/api/admin/glossary/schema"),
        ("GET", "/api/admin/glossary/schema/account"),
        ("POST", "/api/admin/glossary"),
        ("PUT", "/api/admin/glossary/abc"),
        ("PUT", "/api/admin/glossary/abc/enabled"),
        ("DELETE", "/api/admin/glossary/abc"),
        ("POST", "/api/admin/glossary/try"),
        ("GET", "/api/admin/glossary/export"),
        ("POST", "/api/admin/glossary/import"),
    };

    private static HttpRequestMessage Request((string Method, string Path) e)
    {
        var request = new HttpRequestMessage(new HttpMethod(e.Method), e.Path);
        if (e.Method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { enabled = true });
        }
        return request;
    }

    /// <summary>
    /// Tests glossary endpoints as an unregistered user.
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
        /// Verifies that every glossary endpoint looks nonexistent without the permission.
        /// </summary>
        [Fact]
        public async Task EveryEndpoint_LooksNonExistent_WithoutThePermission()
        {
            foreach (var endpoint in Endpoints)
            {
                var response = await _client.SendAsync(Request(endpoint));

                Assert.True(response.StatusCode == HttpStatusCode.NotFound, endpoint.Method + " " + endpoint.Path + " -> " + (int)response.StatusCode);
            }
        }
    }

    /// <summary>
    /// Tests glossary endpoints as an administrator.
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
        /// Verifies that the new permission is listed and held by the administrator.
        /// </summary>
        [Fact]
        public async Task TheNewPermission_IsListed_AndHeldByTheAdministrator()
        {
            var permissions = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/permissions");
            var keys = permissions.EnumerateArray().Select(p => p.GetProperty("key").GetString()).ToList();

            Assert.Contains(Permissions.GlossaryManage, keys);
        }

        /// <summary>
        /// Verifies that the list is empty at first and warns that the store is not persistent.
        /// </summary>
        [Fact]
        public async Task TheList_IsEmptyAtFirst_AndWarnsThatTheStoreIsNotPersistent()
        {
            var list = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/glossary");

            Assert.True(list.GetProperty("enabled").GetBoolean());
            Assert.Equal(0, list.GetProperty("entries").GetArrayLength());
            Assert.False(list.GetProperty("persistent").GetBoolean());
            Assert.Equal(500, list.GetProperty("maxEntries").GetInt32());
        }

        /// <summary>
        /// Verifies that export is a file download and a garbage import is refused with a code.
        /// </summary>
        [Fact]
        public async Task Export_IsAFileDownload_AndAGarbageImportIsRefusedWithACode()
        {
            var export = await _client.GetAsync("/api/admin/glossary/export");
            var import = await _client.PostAsJsonAsync("/api/admin/glossary/import", new { json = "not json", apply = true });
            var body = await import.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Contains("attachment", export.Content.Headers.ContentDisposition?.ToString() ?? export.Headers.ToString());
            Assert.Equal(HttpStatusCode.BadRequest, import.StatusCode);
            Assert.Equal("invalid_json", body.GetProperty("code").GetString());
        }

        /// <summary>
        /// Verifies that an unknown entry answers not found with a code.
        /// </summary>
        [Fact]
        public async Task AnUnknownEntry_AnswersNotFound_WithACode()
        {
            var response = await _client.DeleteAsync("/api/admin/glossary/no-such-entry");
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("not_found", body.GetProperty("code").GetString());
        }
    }
}
