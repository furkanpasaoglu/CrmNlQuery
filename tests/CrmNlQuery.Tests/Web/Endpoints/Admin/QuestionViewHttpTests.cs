using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Composition;
using Microsoft.Extensions.DependencyInjection;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin;

/// <summary>
/// Tests the question text view endpoint against the real HTTP pipeline: off by default, only questions asked while on, and every view audited.
/// </summary>
public class QuestionViewHttpTests
{
    private static AnswerOutcome Answer() => new AnswerOutcome("account", "", "<fetch><entity name=\"account\" /></fetch>", new ResultTable(new[] { "name" }, new List<IReadOnlyList<string>>()), false);

    /// <summary>
    /// Tests the question view endpoint as an administrator.
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

        private AppServices Services => _host.Services.GetRequiredService<AppServices>();

        private async Task SetAsync(string value)
        {
            var result = await Services.Settings.SetAsync("test", "Access:QuestionTextView", value);
            Assert.True(result.Ok, result.Message);
        }

        /// <summary>
        /// Verifies that by default the feature is off and even the administrator gets 404 without any audit record.
        /// </summary>
        [Fact]
        public async Task ByDefault_TheFeatureIsOff_AndEvenTheAdministratorGets404_WithoutAnyAuditRecord()
        {
            await SetAsync("off");
            var user = Guid.NewGuid();
            await Services.Results.PublishAsync(Answer(), user, "asked while off");

            var response = await _client.GetAsync($"/api/admin/users/questions?user={user}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=100");
            Assert.DoesNotContain(audit.EnumerateArray(), a => a.GetProperty("action").GetString() == "questions.view" && a.GetProperty("target").GetString() == user.ToString());
        }

        /// <summary>
        /// Verifies that when on only questions asked while on are shown and every view is audited without the text.
        /// </summary>
        [Fact]
        public async Task WhenOn_OnlyQuestionsAskedWhileOnAreShown_AndEveryViewIsAudited_WithoutTheText()
        {
            var user = Guid.NewGuid();
            await SetAsync("off");
            await Services.Results.PublishAsync(Answer(), user, "asked before it was switched on");
            await SetAsync("on");
            try
            {
                await Services.Results.PublishAsync(Answer(), user, "asked while on");

                var first = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/users/questions?user={user}");
                await _client.GetAsync($"/api/admin/users/questions?user={user}");

                var items = first.GetProperty("questions").EnumerateArray().ToList();
                Assert.Equal("asked while on", Assert.Single(items).GetProperty("question").GetString());
                Assert.Equal("account", items[0].GetProperty("entity").GetString());
                var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=100");
                var views = audit.EnumerateArray().Where(a => a.GetProperty("action").GetString() == "questions.view" && a.GetProperty("target").GetString() == user.ToString()).ToList();
                Assert.Equal(2, views.Count);
                Assert.All(views, v => Assert.Equal(@"lab\boss", v.GetProperty("actor").GetString()));
                Assert.DoesNotContain(views, v => v.GetProperty("detail").GetString()!.Contains("asked"));
            }
            finally
            {
                await SetAsync("off");
            }
        }

        /// <summary>
        /// Verifies that a malformed or missing user is a client error.
        /// </summary>
        [Fact]
        public async Task AMalformedOrMissingUser_IsAClientError()
        {
            await SetAsync("on");
            try
            {
                Assert.True((int)(await _client.GetAsync("/api/admin/users/questions?user=not-a-guid")).StatusCode is >= 400 and < 500);
                Assert.True((int)(await _client.GetAsync("/api/admin/users/questions")).StatusCode is >= 400 and < 500);
            }
            finally
            {
                await SetAsync("off");
            }
        }

        /// <summary>
        /// Verifies that the administrator holds the new permission but the feature stays off until the setting is on.
        /// </summary>
        [Fact]
        public async Task TheAdministrator_HoldsTheNewPermission_ButTheFeatureStaysOffUntilTheSettingIsOn()
        {
            var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/me");
            Assert.Contains("Questions.View", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

            var settings = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
            var item = settings.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "Access")
                .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "Access:QuestionTextView");
            Assert.Equal("off", item.GetProperty("default").GetString());
            Assert.Equal(new[] { "off", "on" }, item.GetProperty("choices").EnumerateArray().Select(c => c.GetString()).ToArray());
        }
    }

    /// <summary>
    /// Tests the question view endpoint as an unregistered user.
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
        /// Verifies that the endpoint looks nonexistent to an unregistered user.
        /// </summary>
        [Fact]
        public async Task TheEndpoint_LooksNonexistent_ToAnUnregisteredUser()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/admin/users/questions?user={Guid.NewGuid()}")).StatusCode);
        }
    }
}
