using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Unit tests for resolving user names from CRM user records.
/// </summary>
public class UserNamesTests
{
    private static (CrmUserNames Names, FakeHandler Handler) Create(string json)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(json));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        return (new CrmUserNames(client), handler);
    }

    /// <summary>
    /// Verifies that returns the full names of the asked users.
    /// </summary>
    [Fact]
    public async Task ReturnsTheFullNamesOfTheAskedUsers()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var json = "{\"value\":[{\"systemuserid\":\"" + a + "\",\"fullname\":\"Ali Veli\"},{\"systemuserid\":\"" + b + "\",\"fullname\":\"Ayşe Yılmaz\"}]}";
        var (names, handler) = Create(json);

        var result = await names.GetNamesAsync(new[] { a, b });

        Assert.Equal("Ali Veli", result[a]);
        Assert.Equal("Ayşe Yılmaz", result[b]);
        var url = Uri.UnescapeDataString(handler.Urls[0]);
        Assert.Contains($"systemuserid eq {a}", url);
        Assert.Contains($"systemuserid eq {b}", url);
    }

    /// <summary>
    /// Verifies that no IDs means no CRM call.
    /// </summary>
    [Fact]
    public async Task NoIds_MeansNoCrmCall()
    {
        var (names, handler) = Create("{\"value\":[]}");
        var result = await names.GetNamesAsync(Array.Empty<Guid>());
        Assert.Empty(result);
        Assert.Empty(handler.Urls);
    }

    /// <summary>
    /// Verifies that users CRM does not know are simply missing.
    /// </summary>
    [Fact]
    public async Task UsersCrmDoesNotKnow_AreSimplyMissing()
    {
        var (names, _) = Create("{\"value\":[]}");
        var result = await names.GetNamesAsync(new[] { Guid.NewGuid() });
        Assert.Empty(result);
    }

    /// <summary>
    /// Verifies that at most fifty IDs are asked for an URL that stays short.
    /// </summary>
    [Fact]
    public async Task AtMostFiftyIdsAreAsked_ForAnUrlThatStaysShort()
    {
        var (names, handler) = Create("{\"value\":[]}");
        await names.GetNamesAsync(Enumerable.Range(0, 80).Select(_ => Guid.NewGuid()));
        var url = Uri.UnescapeDataString(handler.Urls[0]);
        Assert.Equal(50, System.Text.RegularExpressions.Regex.Matches(url, "systemuserid eq ").Count);
    }
}
