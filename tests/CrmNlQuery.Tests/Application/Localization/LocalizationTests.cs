using System.Text.RegularExpressions;
using System.Text.Json;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Preferences.Abstractions;
using CrmNlQuery.Application.Preferences.Services;
using CrmNlQuery.Domain.Common.Constants;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Tests.Application.Localization;

/// <summary>
/// Tests localization infrastructure: language parsing, resolution order, message catalog parity and keyed failure translation.
/// </summary>
public class LocalizationTests
{
    /// <summary>
    /// Verifies that a language is parsed to its primary tag and unsupported ones are none.
    /// </summary>
    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("TR", "tr")]
    [InlineData(" tr-TR ", "tr")]
    [InlineData("en_US", "en")]
    [InlineData("de", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ALanguageIsParsedToItsPrimaryTag_AndUnsupportedOnesAreNone(string? value, string? expected)
    {
        Assert.Equal(expected, UiLanguage.Parse(value));
    }

    /// <summary>
    /// Verifies that the best supported language of the browser header is chosen.
    /// </summary>
    [Theory]
    [InlineData("tr-TR,tr;q=0.9,en;q=0.8", "tr")]
    [InlineData("en-US,en;q=0.9,tr;q=0.8", "en")]
    [InlineData("de,tr;q=0.5", "tr")]
    [InlineData("tr;q=0.2,en;q=0.9", "en")]
    [InlineData("de,fr", null)]
    [InlineData("tr;q=0", null)]
    [InlineData("", null)]
    public void TheBestSupportedLanguageOfTheBrowserHeaderIsChosen(string header, string? expected)
    {
        Assert.Equal(expected, UiLanguage.FromAcceptLanguage(header));
    }

    private static (LanguageResolver Resolver, PreferencesService Preferences) Resolver(Func<string>? defaultLanguage = null)
    {
        var preferences = new PreferencesService(new MemoryPreferencesStore());
        return (new LanguageResolver(preferences, defaultLanguage), preferences);
    }

    /// <summary>
    /// Verifies that the saved preference wins then the browser then the admin default then english.
    /// </summary>
    [Fact]
    public async Task TheSavedPreferenceWins_ThenTheBrowser_ThenTheAdminDefault_ThenEnglish()
    {
        var (resolver, preferences) = Resolver(() => "tr");
        Assert.Equal("tr", await resolver.ResolveAsync("CONTOSO\\alice", null));
        Assert.Equal("en", await resolver.ResolveAsync("CONTOSO\\alice", "en-US,en;q=0.9"));
        await preferences.SetLanguageAsync("CONTOSO\\alice", "en");
        resolver.Invalidate("CONTOSO\\alice");
        Assert.Equal("en", await resolver.ResolveAsync("CONTOSO\\alice", "tr-TR"));
        Assert.Equal("tr", await resolver.ResolveAsync(null, null));
        Assert.Equal("en", await Resolver(() => "xx").Resolver.ResolveAsync("CONTOSO\\bob", null));
    }

    /// <summary>
    /// Verifies that the preference is per account and the account is case insensitive.
    /// </summary>
    [Fact]
    public async Task ThePreferenceIsPerAccount_AndTheAccountIsCaseInsensitive()
    {
        var (resolver, preferences) = Resolver();
        await preferences.SetLanguageAsync("CONTOSO\\Alice", "tr");
        resolver.Invalidate("contoso\\alice");

        Assert.Equal("tr", await resolver.ResolveAsync("contoso\\ALICE", null));
        Assert.Equal("en", await resolver.ResolveAsync("CONTOSO\\bob", null));
    }

    /// <summary>
    /// Verifies that a preference change is seen after invalidation and an unsupported language is refused.
    /// </summary>
    [Fact]
    public async Task APreferenceChange_IsSeenAfterInvalidate_AndAnUnsupportedLanguageIsRefused()
    {
        var (resolver, preferences) = Resolver();
        Assert.Equal("en", await resolver.ResolveAsync("a", null));
        await preferences.SetLanguageAsync("a", "tr");
        Assert.Equal("en", await resolver.ResolveAsync("a", null));
        resolver.Invalidate("a");
        Assert.Equal("tr", await resolver.ResolveAsync("a", null));

        Assert.False((await preferences.SetLanguageAsync("a", "de")).Ok);
        Assert.Equal("tr", await preferences.GetLanguageAsync("a"));
        Assert.True((await preferences.SetLanguageAsync("a", "")).Ok);
        Assert.Null(await preferences.GetLanguageAsync("a"));
    }

    /// <summary>
    /// Verifies that a failing preference store never breaks the request.
    /// </summary>
    [Fact]
    public async Task AFailingPreferenceStore_NeverBreaksTheRequest()
    {
        var resolver = new LanguageResolver(new PreferencesService(new ThrowingStore()), () => "tr");

        Assert.Equal("tr", await resolver.ResolveAsync("CONTOSO\\alice", null));
        Assert.Equal("en", await resolver.ResolveAsync("CONTOSO\\alice", "en"));
    }

    private sealed class ThrowingStore : IPreferencesStore
    {
        /// <summary>
        /// Fake store method that throws when reading a display name.
        /// </summary>
        public Task<string?> GetDisplayNameAsync(Guid owner, CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>
        /// Fake store method that throws when saving a display name.
        /// </summary>
        public Task SetDisplayNameAsync(Guid owner, string? displayName, CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>
        /// Fake store method that throws when reading a language preference.
        /// </summary>
        public Task<string?> GetLanguageAsync(string account, CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>
        /// Fake store method that throws when saving a language preference.
        /// </summary>
        public Task SetLanguageAsync(string account, string? language, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    private static readonly Regex Placeholder = new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Verifies that every message has both languages with the same placeholders and a turkish text that is not just the english one.
    /// </summary>
    [Fact]
    public void EveryMessageHasBothLanguages_WithTheSamePlaceholders_AndATurkishTextThatIsNotJustTheEnglishOne()
    {
        var en = MessageCatalog.For("en");
        var tr = MessageCatalog.For("tr");

        Assert.Equal(en.Keys.OrderBy(k => k), tr.Keys.OrderBy(k => k));
        foreach (var key in en.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]), key + " (en) is empty");
            Assert.False(string.IsNullOrWhiteSpace(tr[key]), key + " (tr) is empty");
            Assert.Equal(Names(en[key]), Names(tr[key]));
            Assert.NotEqual(en[key], tr[key]);
        }
    }

    private static string Names(string text) => string.Join(",", Placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));

    /// <summary>
    /// Verifies that rendering fills parameters falls back to english and never returns empty text.
    /// </summary>
    [Fact]
    public void RenderingFillsParameters_FallsBackToEnglish_AndNeverReturnsEmptyText()
    {
        Assert.Contains("Dil modeline", MessageCatalog.Render(Msg.ModelUnavailable, "tr"));
        Assert.Contains("language model", MessageCatalog.Render(Msg.ModelUnavailable, "en"));
        Assert.Contains("language model", MessageCatalog.Render(Msg.ModelUnavailable, "de"));
        Assert.Equal("msg.doesNotExist", MessageCatalog.Render("msg.doesNotExist", "tr"));
    }

    /// <summary>
    /// Verifies that the english fallback text of a keyed failure is the catalog text and the legacy constants stay in sync.
    /// </summary>
    [Fact]
    public void TheEnglishFallbackTextOfAKeyedFailure_IsTheCatalogText_AndTheLegacyConstantsStayInSync()
    {
        var failure = FailureOutcome.FromKey(Msg.ContextOverflow, "context_overflow");

        Assert.Equal(Msg.ContextOverflow, failure.Key);
        Assert.Equal("context_overflow", failure.Kind);
        Assert.Equal(UserMessages.ContextOverflow, failure.Message);
        Assert.Equal(UserMessages.ModelUnavailable, MessageCatalog.Render(Msg.ModelUnavailable, "en"));
        Assert.Equal(UserMessages.CrmUnavailable, MessageCatalog.Render(Msg.CrmUnavailable, "en"));
        Assert.Equal(UserMessages.ModelOutput, MessageCatalog.Render(Msg.ModelOutput, "en"));
    }

    private static string DtoMessage(AskOutcome outcome, string? language = null)
        => JsonDocument.Parse(JsonSerializer.Serialize(OutcomeDto.From(outcome, language), new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement.GetProperty("message").GetString()!;

    /// <summary>
    /// Verifies that a keyed failure is rendered in the request language and a plain one is left alone.
    /// </summary>
    [Fact]
    public void AKeyedFailureIsRenderedInTheRequestLanguage_AndAPlainOneIsLeftAlone()
    {
        var keyed = FailureOutcome.FromKey(Msg.ModelUnavailable, "llm_unavailable");
        var plain = new FailureOutcome("Some plain English text.");

        Assert.Contains("language model", DtoMessage(keyed));
        Assert.Contains("Dil modeline", DtoMessage(keyed, "tr"));
        RequestLanguage.Set("tr");
        try
        {
            Assert.Contains("Dil modeline", DtoMessage(keyed));
            Assert.Equal("Some plain English text.", DtoMessage(plain));
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }
}
