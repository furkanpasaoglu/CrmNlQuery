using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin;

/// <summary>
/// HTTP tests that the confirmation rule for risky AD group mappings also applies to direct requests: unconfirmed Administrator mapping returns 409 confirm_required, confirm=true succeeds.
/// </summary>
public class GroupMappingConfirmHttpTests
{
    /// <summary>
    /// Nested HTTP test class running the group-mapping confirmation scenarios as an administrator on the shared test host.
    /// </summary>
    [Collection("WebHost")]
    public class AsAdministrator : IClassFixture<AppUseHttpTests.BossHost>
    {
        private readonly HttpClient _client;
        private const string Group = @"LAB\ConfirmTest";

        public AsAdministrator(AppUseHttpTests.BossHost host)
        {
            _client = host.CreateClient();
        }

        private string Url(bool confirm) => "/api/admin/groups/roles?group=" + Uri.EscapeDataString(Group) + (confirm ? "&confirm=true" : "");

        /// <summary>
        /// Verifies that an unconfirmed administrator mapping gets409 with the code and nothing is saved then confirmed it works.
        /// </summary>
        [Fact]
        public async Task AnUnconfirmedAdministratorMapping_Gets409WithTheCode_AndNothingIsSaved_ThenConfirmedItWorks()
        {
            var body = new { roleIds = new[] { AccessRoles.AdministratorId } };

            var refused = await _client.PutAsJsonAsync(Url(false), body);

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var json = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("confirm_required", json.GetProperty("code").GetString());
            Assert.Contains("administrator", json.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            var groups = await _client.GetFromJsonAsync<JsonElement>("/api/admin/groups");
            Assert.DoesNotContain(groups.EnumerateArray(), g => g.GetProperty("group").GetString() == Group.ToLowerInvariant());

            try
            {
                var accepted = await _client.PutAsJsonAsync(Url(true), body);
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=50");
                Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("action").GetString() == "group.roles" && a.GetProperty("detail").GetString()!.Contains("CONFIRMED administrator grant"));
            }
            finally
            {
                await _client.DeleteAsync("/api/admin/groups?group=" + Uri.EscapeDataString(Group));
            }
        }
    }
}
