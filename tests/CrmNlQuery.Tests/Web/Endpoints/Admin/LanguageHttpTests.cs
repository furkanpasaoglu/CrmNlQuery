using System.Net.Http.Json;
using System.Text.Json;
using CrmNlQuery.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace CrmNlQuery.Tests.Web.Endpoints.Admin;

/// <summary>
/// Tests language resolution against the real HTTP pipeline: preference, Accept-Language, then default.
/// </summary>
[Collection("WebHost")]
public class LanguageHttpTests : IClassFixture<AppUseHttpTests.BossHost>
{
    private readonly AppUseHttpTests.BossHost _host;
    private readonly HttpClient _client;

    public LanguageHttpTests(AppUseHttpTests.BossHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    private AppServices Services => _host.Services.GetRequiredService<AppServices>();

    private async Task<string> LanguageAsync(string? acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/me");
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("language").GetString()!;
    }

    /// <summary>
    /// Verifies that the language follows the preference then the browser header then the default.
    /// </summary>
    [Fact]
    public async Task TheLanguageFollowsThePreference_ThenTheBrowserHeader_ThenTheDefault()
    {
        var account = AppUseHttpTests.Boss;
        await Services.Preferences.SetLanguageAsync(account, null);
        Services.Language.Invalidate(account);

        Assert.Equal("en", await LanguageAsync(null));
        Assert.Equal("tr", await LanguageAsync("tr-TR,tr;q=0.9,en;q=0.8"));

        await Services.Preferences.SetLanguageAsync(account, "tr");
        Services.Language.Invalidate(account);
        Assert.Equal("tr", await LanguageAsync(null));
        Assert.Equal("tr", await LanguageAsync("en-US"));

        await Services.Preferences.SetLanguageAsync(account, null);
        Services.Language.Invalidate(account);
    }

    /// <summary>
    /// Verifies that the panel can save its language for the account and an unsupported one is refused.
    /// </summary>
    [Fact]
    public async Task ThePanelCanSaveItsLanguage_ForTheAccount_AndAnUnsupportedOneIsRefused()
    {
        var account = AppUseHttpTests.Boss;
        await Services.Preferences.SetLanguageAsync(account, null);
        Services.Language.Invalidate(account);

        var saved = await _client.PutAsJsonAsync("/api/admin/me/language", new { language = "tr" });
        Assert.Equal(System.Net.HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tr", body.GetProperty("language").GetString());
        Assert.Equal("tr", body.GetProperty("effectiveLanguage").GetString());
        Assert.Equal("tr", await LanguageAsync(null));
        Assert.Equal("tr", await Services.Preferences.GetLanguageAsync(account));

        var refused = await _client.PutAsJsonAsync("/api/admin/me/language", new { language = "de" });
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("tr", await Services.Preferences.GetLanguageAsync(account));

        var cleared = await _client.PutAsJsonAsync("/api/admin/me/language", new { language = "" });
        Assert.Equal(System.Net.HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null(await Services.Preferences.GetLanguageAsync(account));
        Services.Language.Invalidate(account);
    }

    /// <summary>
    /// Verifies that the panel announces the supported languages.
    /// </summary>
    [Fact]
    public async Task ThePanelAnnouncesTheSupportedLanguages()
    {
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/admin/me");

        Assert.Equal(new[] { "en", "tr" }, me.GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ToArray());
    }

    /// <summary>
    /// Verifies that the default language setting is a choice under the interface group.
    /// </summary>
    [Fact]
    public async Task TheDefaultLanguageSetting_IsAChoiceUnderTheInterfaceGroup()
    {
        var settings = await _client.GetFromJsonAsync<JsonElement>("/api/admin/settings");

        var text = settings.ToString();
        Assert.Contains("Ui:DefaultLanguage", text);
        Assert.Contains("Interface", text);
    }
}
