using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CrmNlQuery.Composition;
using Microsoft.Extensions.DependencyInjection;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin.Tools;

/// <summary>
/// Tests the export endpoint against the real HTTP pipeline: download headers, content, audit record with SHA-256 and bad requests.
/// </summary>
[Collection("WebHost")]
public class ExportHttpTests : IClassFixture<AppUseHttpTests.BossHost>
{
    private readonly AppUseHttpTests.BossHost _host;
    private readonly HttpClient _client;

    public ExportHttpTests(AppUseHttpTests.BossHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    private async Task<DataAccessRecord> SeedAsync(Guid user)
    {
        var record = new DataAccessRecord(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), user, "ask", new[] { "account", "contact" }, 5, "ok", Guid.NewGuid());
        await _host.Services.GetRequiredService<AppServices>().AccessLog.AddAsync(record);
        return record;
    }

    /// <summary>
    /// Verifies that a download is an attachment with the records and the audit trail holds the SHA-256 of those bytes.
    /// </summary>
    [Fact]
    public async Task ADownload_IsAnAttachment_WithTheRecords_AndTheAuditTrailHoldsTheSha256OfThoseBytes()
    {
        var user = Guid.NewGuid();
        await SeedAsync(user);

        var response = await _client.GetAsync($"/api/admin/export/accesslog?user={user}&tz=Europe/Istanbul");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition!.ToString());
        Assert.EndsWith(".csv\"", response.Content.Headers.ContentDisposition!.ToString());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(user.ToString("D"), text);
        Assert.Contains("account;contact", text);

        var audit = await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=100");
        var entries = audit.EnumerateArray().ToList();
        Assert.Contains(entries, a => a.GetProperty("action").GetString() == "export.start" && a.GetProperty("detail").GetString()!.Contains(user.ToString("D")));
        var done = entries.First(a => a.GetProperty("action").GetString() == "export.done" && a.GetProperty("detail").GetString()!.Contains($"bytes={bytes.Length};"));
        Assert.Contains("sha256=" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), done.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Verifies that JSON Lines output is one object per line.
    /// </summary>
    [Fact]
    public async Task JsonLines_IsOneObjectPerLine()
    {
        var user = Guid.NewGuid();
        await SeedAsync(user);

        var response = await _client.GetAsync($"/api/admin/export/accesslog?user={user}&format=jsonl");

        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal(5, JsonDocument.Parse(lines[0]).RootElement.GetProperty("rows").GetInt32());
    }

    /// <summary>
    /// Verifies that invalid export requests are answered with the expected error status codes.
    /// </summary>
    [Theory]
    [InlineData("/api/admin/export/nonsense", HttpStatusCode.BadRequest)]
    [InlineData("/api/admin/export/audit?tz=%3Cscript%3E", HttpStatusCode.BadRequest)]
    [InlineData("/api/admin/export/audit?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z", HttpStatusCode.BadRequest)]
    [InlineData("/api/admin/export/audit?kind=user.%25", HttpStatusCode.BadRequest)]
    public async Task BadRequests_AreClientErrors_AndNoFileIsSent(string url, HttpStatusCode expected)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    /// <summary>
    /// Verifies that check validates the request without sending a file and writes no audit record.
    /// </summary>
    [Fact]
    public async Task Check_ValidatesWithoutSendingAFile_AndWritesNoAuditRecord()
    {
        var before = (await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=200")).EnumerateArray().Count(e => e.GetProperty("action").GetString() == "export.start");

        var ok = await _client.GetAsync("/api/admin/export/usage?check=true");
        var bad = await _client.GetAsync("/api/admin/export/usage?check=true&user=" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/json", ok.Content.Headers.ContentType!.MediaType);
        Assert.Null(ok.Content.Headers.ContentDisposition);
        Assert.EndsWith(".csv", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileName").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var after = (await _client.GetFromJsonAsync<JsonElement>("/api/admin/audit?take=200")).EnumerateArray().Count(e => e.GetProperty("action").GetString() == "export.start");
        Assert.Equal(before, after);
    }

    /// <summary>
    /// Verifies that the schema lists every data set with its columns.
    /// </summary>
    [Fact]
    public async Task TheSchema_ListsEveryDataSetWithItsColumns()
    {
        var schema = await _client.GetFromJsonAsync<JsonElement>("/api/admin/export/schema");

        var sets = schema.EnumerateArray().ToDictionary(s => s.GetProperty("dataset").GetString()!);
        Assert.Equal(new[] { "audit", "accesslog", "usage", "feedback" }.OrderBy(x => x), sets.Keys.OrderBy(x => x));
        Assert.Contains(sets["accesslog"].GetProperty("columns").EnumerateArray(), c => c.GetProperty("name").GetString() == "rows" && c.GetProperty("type").GetString() == "number");
        Assert.All(sets["audit"].GetProperty("columns").EnumerateArray(), c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("description").GetString())));
    }
}
