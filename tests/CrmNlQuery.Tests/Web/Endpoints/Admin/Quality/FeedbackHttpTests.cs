using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Composition;
using Microsoft.Extensions.DependencyInjection;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Feedback.Constants;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin.Quality;

/// <summary>
/// Tests the feedback admin endpoints against the real HTTP pipeline: summary, shared questions without user names, audited views and 404 without permission.
/// </summary>
public class FeedbackHttpTests
{
    private static AnswerOutcome Answer() => new AnswerOutcome("account", "", "<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", new ResultTable(new[] { "name" }, new List<IReadOnlyList<string>>()), false);

    /// <summary>
    /// Tests the feedback endpoints as an administrator.
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

        /// <summary>
        /// Verifies that shared questions are listed without a user every view is audited and they can be deleted.
        /// </summary>
        [Fact]
        public async Task SharedQuestions_AreListedWithoutAUser_EveryViewIsAudited_AndTheyCanBeDeleted()
        {
            var user = Guid.NewGuid();
            var published = await Services.Results.PublishAsync(Answer(), user, "which accounts are in Paris");
            await Services.Feedback.RateAsync(user, published.ResultId!, "down", FeedbackReasons.WrongRecords, true);

            var first = await _client.GetFromJsonAsync<JsonElement>("/api/admin/feedback/candidates");
            await _client.GetAsync("/api/admin/feedback/candidates");

            var mine = first.GetProperty("candidates").EnumerateArray().Single(c => c.GetProperty("id").GetString() == published.ResultId);
            Assert.Equal("which accounts are in Paris", mine.GetProperty("question").GetString());
            Assert.Equal("wrong_records", mine.GetProperty("reason").GetString());
            Assert.DoesNotContain(user.ToString(), first.ToString());
            Assert.DoesNotContain("owner", first.ToString(), StringComparison.OrdinalIgnoreCase);
            var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=100");
            var views = audit.EnumerateArray().Where(a => a.GetProperty("action").GetString() == "feedback.view").ToList();
            Assert.True(views.Count >= 2);
            Assert.All(views, v => Assert.DoesNotContain("Paris", v.GetProperty("detail").GetString()!));

            var deleted = await _client.DeleteAsync($"/api/admin/feedback/candidates/{published.ResultId}");
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/admin/feedback/candidates/{published.ResultId}")).StatusCode);
        }

        /// <summary>
        /// Verifies that the summary counts votes and reasons without any question text.
        /// </summary>
        [Fact]
        public async Task TheSummary_CountsVotesAndReasons_WithoutAnyQuestionText()
        {
            var user = Guid.NewGuid();
            var published = await Services.Results.PublishAsync(Answer(), user, "secret wording here");
            await Services.Feedback.RateAsync(user, published.ResultId!, "down", FeedbackReasons.TooSlow, false);

            var summary = await _client.GetFromJsonAsync<JsonElement>("/api/admin/feedback/summary?days=7");

            Assert.True(summary.GetProperty("down").GetInt32() >= 1);
            Assert.Contains(summary.GetProperty("reasons").EnumerateArray(), r => r.GetProperty("reason").GetString() == "too_slow");
            Assert.DoesNotContain("secret", summary.ToString());
        }
    }

    /// <summary>
    /// Tests the feedback endpoints as a user without permission.
    /// </summary>
    [Collection("WebHost")]
    public class AsStranger : IClassFixture<AppUseHttpTests.StrangerHost>
    {
        private readonly HttpClient _client;

        public AsStranger(AppUseHttpTests.StrangerHost host)
        {
            _client = host.CreateClient();
        }

        /// <summary>
        /// Verifies that without the permission every admin feedback endpoint returns 404.
        /// </summary>
        [Fact]
        public async Task WithoutThePermission_EveryAdminFeedbackEndpoint_Is404()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/feedback/summary")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/feedback/candidates")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/admin/feedback/candidates/abc")).StatusCode);
        }
    }
}
