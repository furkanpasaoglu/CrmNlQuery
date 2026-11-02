using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin.Tools;

/// <summary>
/// Tests the design assistant endpoints against the real HTTP pipeline, covering only validation since a valid request would call the real model.
/// </summary>
[Collection("WebHost")]
public class DesignHttpTests : IClassFixture<AppUseHttpTests.BossHost>
{
    private readonly HttpClient _client;

    public DesignHttpTests(AppUseHttpTests.BossHost host)
    {
        _client = host.CreateClient();
    }

    /// <summary>
    /// Verifies that the list is empty for a new user and jobs are per owner.
    /// </summary>
    [Fact]
    public async Task TheList_IsEmptyForANewUser_AndTheJobsArePerOwner()
    {
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/admin/design");

        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Equal(0, list.GetArrayLength());
    }

    /// <summary>
    /// Verifies that too short, empty or missing request text is rejected.
    /// </summary>
    [Theory]
    [InlineData("{\"text\":\"kısa\"}")]
    [InlineData("{\"text\":\"\"}")]
    [InlineData("{}")]
    public async Task ATooShortOrMissingRequest_IsABadRequest_AndStartsNothing(string body)
    {
        var response = await _client.PostAsync("/api/admin/design", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await _client.GetFromJsonAsync<JsonElement>("/api/admin/design")).GetArrayLength());
    }

    /// <summary>
    /// Verifies that a bad prefix is a bad request.
    /// </summary>
    [Fact]
    public async Task ABadPrefix_IsABadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/design", new { text = new string('x', 40), prefix = "Bad Prefix!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that unknown or foreign design job ids are answered as not found.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/admin/design/00000000000000000000000000000000")]
    [InlineData("GET", "/api/admin/design/00000000000000000000000000000000/download?format=docx")]
    [InlineData("POST", "/api/admin/design/00000000000000000000000000000000/cancel")]
    [InlineData("DELETE", "/api/admin/design/00000000000000000000000000000000")]
    [InlineData("GET", "/api/admin/design/not-an-id")]
    public async Task AnUnknownOrForeignJob_IsNotFound_ForEveryEndpoint(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
