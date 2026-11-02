using System.Net;
using System.Net.Http.Json;
using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin.Data;

/// <summary>
/// Tests the data policy endpoints against the real HTTP pipeline with a fixed identity.
/// </summary>
public class DataPolicyHttpTests
{
    private static readonly (string Method, string Path)[] Endpoints =
    {
        ("GET", "/api/admin/data-policy/tables"),
        ("GET", "/api/admin/data-policy/review"),
        ("GET", "/api/admin/data-policy/tables/account/fields"),
        ("PUT", "/api/admin/data-policy/tables/account"),
        ("PUT", "/api/admin/data-policy/tables/account/fields/name"),
        ("POST", "/api/admin/data-policy/tables/account/check"),
    };

    private static HttpRequestMessage Request((string Method, string Path) e)
    {
        var request = new HttpRequestMessage(new HttpMethod(e.Method), e.Path);
        if (e.Method == "PUT")
        {
            request.Content = JsonContent.Create(new { allowed = false });
        }
        return request;
    }

    /// <summary>
    /// Tests data policy endpoints as an unauthorized stranger.
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
        /// Verifies that every endpoint looks non existent without the permission.
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
    /// Tests data policy endpoints as an administrator.
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

            Assert.Contains(Permissions.DataPolicyManage, keys);
        }

        /// <summary>
        /// Verifies that when the CRM schema cannot be read the lists answer503 not an empty list.
        /// </summary>
        [Fact]
        public async Task WhenTheCrmSchemaCannotBeRead_TheListsAnswer503_NotAnEmptyList()
        {
            var tables = await _client.GetAsync("/api/admin/data-policy/tables");
            var review = await _client.GetAsync("/api/admin/data-policy/review");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, tables.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, review.StatusCode);
        }
    }
}
