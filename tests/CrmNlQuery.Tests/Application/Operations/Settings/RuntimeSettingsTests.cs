using CrmNlQuery.Application.Operations.Settings;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Tests.Application.Operations.Settings;

/// <summary>
/// Tests runtime settings precedence, validation, auditing, refresh behaviour and the settings catalog.
/// </summary>
public class RuntimeSettingsTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FlakyStore : ISettingsStore
    {
        private readonly MemorySettingsStore _inner = new MemorySettingsStore();
        public bool Broken { get; set; }
        public int Reads { get; private set; }

        /// <summary>
        /// Returns all stored setting overrides from the in-memory settings store.
        /// </summary>
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        {
            Reads++;
            if (Broken) throw new InvalidOperationException("database down");
            return _inner.GetAllAsync(ct);
        }

        /// <summary>
        /// Stores a setting override in the in-memory settings store.
        /// </summary>
        public Task SetAsync(string key, string value, string actor, DateTimeOffset at, CancellationToken ct = default)
        {
            if (Broken) throw new InvalidOperationException("database down");
            return _inner.SetAsync(key, value, actor, at, ct);
        }

        /// <summary>
        /// Removes a setting override from the in-memory settings store.
        /// </summary>
        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            if (Broken) throw new InvalidOperationException("database down");
            return _inner.RemoveAsync(key, ct);
        }
    }

    private readonly Clock _clock = new Clock();
    private readonly FlakyStore _store = new FlakyStore();
    private readonly List<AuditEntry> _audit = new List<AuditEntry>();
    private readonly List<Exception> _errors = new List<Exception>();
    private readonly Dictionary<string, string> _file = new Dictionary<string, string>();

    private RuntimeSettings Settings()
        => new RuntimeSettings(_store, key => _file.TryGetValue(key, out var v) ? v : null,
            (entry, ct) => { _audit.Add(entry); return Task.CompletedTask; }, () => _clock.Now, TimeSpan.FromSeconds(30), _errors.Add);

    private RuntimeSettings SettingsWithTtl(TimeSpan ttl)
        => new RuntimeSettings(_store, key => _file.TryGetValue(key, out var v) ? v : null,
            (entry, ct) => { _audit.Add(entry); return Task.CompletedTask; }, () => _clock.Now, ttl, _errors.Add);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Verifies that setting values are validated against their allowed ranges and formats.
    /// </summary>
    [Theory]
    [InlineData("Limits:QuestionsPerMinute", "30", true)]
    [InlineData("Limits:QuestionsPerMinute", " 30 ", true)]
    [InlineData("Limits:QuestionsPerMinute", "0", false)]
    [InlineData("Limits:QuestionsPerMinute", "601", false)]
    [InlineData("Limits:QuestionsPerMinute", "-5", false)]
    [InlineData("Limits:QuestionsPerMinute", "abc", false)]
    [InlineData("Limits:QuestionsPerMinute", "", false)]
    [InlineData("Limits:CloudDailyTokenBudget", "0", true)]
    [InlineData("Limits:AskEnabled", "false", true)]
    [InlineData("Limits:AskEnabled", "TRUE", true)]
    [InlineData("Limits:AskEnabled", "yes", false)]
    public void Validate_AcceptsOnlyValuesInsideTheAllowedRange(string key, string raw, bool ok)
    {
        var definition = SettingsCatalog.Find(key)!;

        Assert.Equal(ok, SettingsCatalog.Validate(definition, raw).Ok);
    }

    /// <summary>
    /// Verifies that validation returns the normalized form of a value.
    /// </summary>
    [Fact]
    public void Validate_ReturnsTheNormalizedForm()
    {
        Assert.Equal("true", SettingsCatalog.Validate(SettingsCatalog.Find("Limits:AskEnabled")!, "TRUE").Value);
        Assert.Equal("30", SettingsCatalog.Validate(SettingsCatalog.Find("Limits:QuestionsPerMinute")!, " 30 ").Value);
    }

    /// <summary>
    /// Verifies that ShowFetchXml is a feature flag that is off by default.
    /// </summary>
    [Fact]
    public void ShowFetchXml_IsAFeatureFlag_OffByDefault()
    {
        var flag = SettingsCatalog.Find("Features:ShowFetchXml")!;

        Assert.Equal(SettingKind.Boolean, flag.Kind);
        Assert.Equal("false", flag.Default);
        Assert.Contains(flag.Group, SettingsCatalog.Groups);
    }

    /// <summary>
    /// Verifies that OpenInCrm is a feature flag that is off by default.
    /// </summary>
    [Fact]
    public void OpenInCrm_IsAFeatureFlag_OffByDefault()
    {
        var flag = SettingsCatalog.Find("Features:OpenInCrm")!;

        Assert.Equal(SettingKind.Boolean, flag.Kind);
        Assert.Equal("false", flag.Default);
        Assert.Contains(flag.Group, SettingsCatalog.Groups);
    }

    /// <summary>
    /// Verifies that the settings catalog never lists secrets, identity or connection settings.
    /// </summary>
    [Fact]
    public void TheCatalog_NeverListsSecretsIdentityOrConnectionSettings()
    {
        var allowedPrefixes = new[] { "Limits:", "Knowledge:", "Planning:", "Conversation:", "History:RetentionDays", "Metrics:RetentionDays", "Features:", "Create:", "Counting:", "Access:RequireAppUse", "Access:DataPolicy", "Access:QuestionTextView", "Retention:SecurityLogDays", "Feedback:", "Retention:FeedbackDays", "Retention:DesignDays", "Retention:QualityRunDays", "Summary:", "Metadata:", "Export:", "Design:", "Ui:", "Quality:", "Glossary:", "Reports:" };

        Assert.All(SettingsCatalog.All, d => Assert.Contains(allowedPrefixes, prefix => d.Key.StartsWith(prefix)));
        Assert.DoesNotContain(SettingsCatalog.All, d => d.Key.Contains("ApiKey") || d.Key.Contains("Identity") || d.Key.Contains("Connection")
            || d.Key.Contains("Cors") || d.Key.Contains("AllowCloud") || d.Key.Contains("Bootstrap") || d.Key.Contains("Password") || d.Key.Contains("EmbeddingModel"));
    }

    /// <summary>
    /// Verifies that the catalog defaults match the defaults used in code.
    /// </summary>
    [Fact]
    public void TheCatalogDefaults_MatchTheCodeDefaults()
    {
        foreach (var definition in SettingsCatalog.All.Where(d => SettingsCatalog.IsLimitKey(d.Key)))
        {
            Assert.Equal(SettingsCatalog.Read(new GuardrailLimits(), definition.Key), definition.Default);
        }
        var options = new CrmNlQuery.Application.Knowledge.Models.KnowledgeOptions();
        Assert.Equal(options.Enabled ? "true" : "false", SettingsCatalog.Find(SettingsCatalog.KnowledgeEnabled)!.Default);
        Assert.Equal("0.5", SettingsCatalog.Find(SettingsCatalog.KnowledgeMinSimilarity)!.Default);
        Assert.Equal(options.MinSimilarity, double.Parse(SettingsCatalog.Find(SettingsCatalog.KnowledgeMinSimilarity)!.Default, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(options.TopK.ToString(), SettingsCatalog.Find(SettingsCatalog.KnowledgeTopK)!.Default);
        Assert.Equal(options.MaxChars.ToString(), SettingsCatalog.Find(SettingsCatalog.KnowledgeMaxChars)!.Default);
    }

    /// <summary>
    /// Verifies that the Knowledge:MinSimilarity setting accepts only values in its allowed range.
    /// </summary>
    [Theory]
    [InlineData("Knowledge:MinSimilarity", "0.5", true)]
    [InlineData("Knowledge:MinSimilarity", "0", true)]
    [InlineData("Knowledge:MinSimilarity", "1", true)]
    [InlineData("Knowledge:MinSimilarity", "1.01", false)]
    [InlineData("Knowledge:MinSimilarity", "-0.1", false)]
    [InlineData("Knowledge:MinSimilarity", "0,5", false)]
    [InlineData("Knowledge:MinSimilarity", "abc", false)]
    [InlineData("Knowledge:TopK", "11", false)]
    [InlineData("Conversation:ContextMinutes", "30", true)]
    [InlineData("Conversation:ContextMinutes", "5", true)]
    [InlineData("Conversation:ContextMinutes", "240", true)]
    [InlineData("Conversation:ContextMinutes", "4", false)]
    [InlineData("Conversation:ContextMinutes", "241", false)]
    [InlineData("Planning:CacheMinutes", "0", true)]
    [InlineData("Planning:CacheMinutes", "10081", false)]
    [InlineData("History:RetentionDays", "0", true)]
    [InlineData("Metrics:RetentionDays", "3651", false)]
    public void TheNewSettings_AreValidatedLikeTheOthers(string key, string raw, bool ok)
    {
        Assert.Equal(ok, SettingsCatalog.Validate(SettingsCatalog.Find(key)!, raw).Ok);
    }

    /// <summary>
    /// Verifies that a decimal setting is stored in a normalized invariant-culture form.
    /// </summary>
    [Fact]
    public void ADecimal_IsStoredInANormalizedInvariantForm()
    {
        Assert.Equal("0.65", SettingsCatalog.Validate(SettingsCatalog.Find("Knowledge:MinSimilarity")!, "0.650").Value);
    }

    /// <summary>
    /// Verifies that the typed getters follow the precedence panel override, then file value, then default.
    /// </summary>
    [Fact]
    public async Task TheTypedGetters_FollowTheSamePrecedence_PanelThenFileThenDefault()
    {
        _file["Knowledge:TopK"] = "6";
        var settings = Settings();

        Assert.Equal(6, settings.GetInt("Knowledge:TopK"));
        Assert.Equal(720, settings.GetInt("Planning:CacheMinutes"));
        Assert.False(settings.GetBool("Knowledge:Enabled"));
        Assert.Equal(0.5m, settings.GetDecimal("Knowledge:MinSimilarity"));

        await settings.SetAsync("a", "Knowledge:TopK", "3");
        await settings.SetAsync("a", "Knowledge:Enabled", "true");
        await settings.SetAsync("a", "Knowledge:MinSimilarity", "0.62");

        Assert.Equal(3, settings.GetInt("Knowledge:TopK"));
        Assert.True(settings.GetBool("Knowledge:Enabled"));
        Assert.Equal(0.62m, settings.GetDecimal("Knowledge:MinSimilarity"));
    }

    /// <summary>
    /// Verifies that an unknown setting key is treated as a programming error rather than silently defaulting.
    /// </summary>
    [Fact]
    public void AnUnknownKey_IsAProgrammingError_NotASilentDefault()
    {
        Assert.Throws<ArgumentException>(() => Settings().Get("Nope:Nothing"));
    }

    /// <summary>
    /// Verifies that changes to non limit settings are audited and leave the limits alone.
    /// </summary>
    [Fact]
    public async Task ChangesToNonLimitSettings_AreAudited_AndLeaveTheLimitsAlone()
    {
        var settings = Settings();
        var before = settings.Limits;

        await settings.SetAsync(@"contoso\admin", "Knowledge:TopK", "2");

        Assert.Equal("5 -> 2", _audit.Single().Detail);
        Assert.Equal(before, settings.Limits);
    }

    /// <summary>
    /// Verifies that resetting a non-limit setting returns it to the file value.
    /// </summary>
    [Fact]
    public async Task ResettingANonLimitSetting_GoesBackToTheFileValue()
    {
        _file["Planning:CacheMinutes"] = "60";
        var settings = Settings();
        await settings.SetAsync("a", "Planning:CacheMinutes", "5");

        await settings.ResetAsync("a", "Planning:CacheMinutes");

        Assert.Equal(60, settings.GetInt("Planning:CacheMinutes"));
    }

    /// <summary>
    /// Verifies that every limit key round trips through apply and read.
    /// </summary>
    [Fact]
    public void EveryLimitKey_RoundTripsThroughApplyAndRead()
    {
        foreach (var definition in SettingsCatalog.All.Where(d => SettingsCatalog.IsLimitKey(d.Key)))
        {
            var sample = definition.Kind == SettingKind.Boolean ? "false" : definition.Max.ToString();
            var limits = SettingsCatalog.Apply(new GuardrailLimits(), definition.Key, sample);

            Assert.Equal(sample, SettingsCatalog.Read(limits, definition.Key));
        }
    }

    /// <summary>
    /// Verifies that with no override and no file value the code defaults apply.
    /// </summary>
    [Fact]
    public void WithNoOverrideAndNoFileValue_TheCodeDefaultsApply()
    {
        Assert.Equal(new GuardrailLimits(), Settings().Limits);
    }

    /// <summary>
    /// Verifies that a file value beats the default.
    /// </summary>
    [Fact]
    public void AFileValue_BeatsTheDefault()
    {
        _file["Limits:QuestionsPerMinute"] = "50";

        Assert.Equal(50, Settings().Limits.QuestionsPerMinute);
    }

    /// <summary>
    /// Verifies that an invalid file value is ignored and the default applies.
    /// </summary>
    [Fact]
    public void AnInvalidFileValue_IsIgnored_AndTheDefaultApplies()
    {
        _file["Limits:QuestionsPerMinute"] = "-1";
        _file["Limits:MaxConditions"] = "many";

        var limits = Settings().Limits;

        Assert.Equal(20, limits.QuestionsPerMinute);
        Assert.Equal(25, limits.MaxConditions);
    }

    /// <summary>
    /// Verifies that a panel override beats the file value.
    /// </summary>
    [Fact]
    public async Task APanelOverride_BeatsTheFileValue()
    {
        _file["Limits:QuestionsPerMinute"] = "50";
        var settings = Settings();

        var result = await settings.SetAsync(@"contoso\admin", "Limits:QuestionsPerMinute", "7");

        Assert.True(result.Ok);
        Assert.Equal(7, settings.Limits.QuestionsPerMinute);
        Assert.Equal("panel", settings.GetStates().Single(s => s.Definition.Key == "Limits:QuestionsPerMinute").Source);
    }

    /// <summary>
    /// Verifies that resetting an override falls back to the file value.
    /// </summary>
    [Fact]
    public async Task ResettingAnOverride_FallsBackToTheFileValue()
    {
        _file["Limits:QuestionsPerMinute"] = "50";
        var settings = Settings();
        await settings.SetAsync("a", "Limits:QuestionsPerMinute", "7");

        var result = await settings.ResetAsync("a", "Limits:QuestionsPerMinute");

        Assert.True(result.Ok);
        Assert.Equal(50, settings.Limits.QuestionsPerMinute);
        Assert.Equal("file", settings.GetStates().Single(s => s.Definition.Key == "Limits:QuestionsPerMinute").Source);
    }

    /// <summary>
    /// Verifies that an unknown or forbidden setting is refused and nothing is stored or audited.
    /// </summary>
    [Fact]
    public async Task AnUnknownOrForbiddenSetting_IsRefused_AndNothingIsStoredOrAudited()
    {
        var settings = Settings();

        foreach (var key in new[] { "Anthropic:ApiKey", "Identity:Mode", "Llm:AllowCloud", "History:ConnectionString", "Limits:Nope" })
        {
            var result = await settings.SetAsync("a", key, "x");
            Assert.Equal(AccessError.Invalid, result.Error);
        }
        Assert.Empty(await _store.GetAllAsync());
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that an out of range value is refused with a message.
    /// </summary>
    [Fact]
    public async Task AnOutOfRangeValue_IsRefused_WithAMessage()
    {
        var result = await Settings().SetAsync("a", "Limits:QuestionsPerMinute", "100000");

        Assert.Equal(AccessError.Invalid, result.Error);
        Assert.Contains("between 1 and 600", result.Message);
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that every change is audited with the old and new value.
    /// </summary>
    [Fact]
    public async Task EveryChange_IsAudited_WithTheOldAndNewValue()
    {
        var settings = Settings();

        await settings.SetAsync(@"contoso\admin", "Limits:QuestionsPerMinute", "7");
        await settings.ResetAsync(@"contoso\admin", "Limits:QuestionsPerMinute");

        Assert.Equal("setting.set", _audit[0].Action);
        Assert.Equal("Limits:QuestionsPerMinute", _audit[0].Target);
        Assert.Equal("20 -> 7", _audit[0].Detail);
        Assert.Equal(@"contoso\admin", _audit[0].Actor);
        Assert.Equal("setting.reset", _audit[1].Action);
        Assert.Equal("7 -> 20", _audit[1].Detail);
    }

    /// <summary>
    /// Verifies that a pause switch takes effect immediately.
    /// </summary>
    [Fact]
    public async Task APauseSwitch_TakesEffectImmediately()
    {
        var settings = Settings();
        Assert.True(settings.Limits.AskEnabled);

        await settings.SetAsync("a", SettingsCatalog.AskEnabled, "false");

        Assert.False(settings.Limits.AskEnabled);
    }

    /// <summary>
    /// Verifies that initialization loads the stored overrides.
    /// </summary>
    [Fact]
    public async Task Initialize_LoadsTheStoredOverrides()
    {
        await _store.SetAsync("Limits:QuestionsPerDay", "99", "a", _clock.Now);
        var settings = Settings();

        await settings.InitializeAsync();

        Assert.Equal(99, settings.Limits.QuestionsPerDay);
    }

    /// <summary>
    /// Verifies that if the store cannot be read, the restrictive file or default values apply rather than unlimited.
    /// </summary>
    [Fact]
    public async Task IfTheStoreCannotBeRead_TheRestrictiveFileOrDefaultValuesApply_NotUnlimited()
    {
        _store.Broken = true;
        _file["Limits:QuestionsPerMinute"] = "40";
        var settings = Settings();

        await settings.InitializeAsync();

        Assert.Equal(40, settings.Limits.QuestionsPerMinute);
        Assert.Equal(500, settings.Limits.QuestionsPerDay);
        Assert.Single(_errors);
    }

    /// <summary>
    /// Verifies that if the store breaks later, the last known values stay and the refresh loop keeps running.
    /// </summary>
    [Fact]
    public async Task IfTheStoreBreaksLater_TheLastKnownValuesStay_AndTheLoopKeepsRunning()
    {
        await _store.SetAsync("Limits:QuestionsPerDay", "99", "a", _clock.Now);
        var settings = SettingsWithTtl(TimeSpan.FromMilliseconds(30));
        await settings.InitializeAsync();
        using var stop = new CancellationTokenSource();
        var loop = settings.RunRefreshLoopAsync(stop.Token);
        _store.Broken = true;

        await WaitUntil(() => _errors.Count >= 2);

        Assert.True(_errors.Count >= 2);
        Assert.Equal(99, settings.Limits.QuestionsPerDay);
        stop.Cancel();
        await loop;
    }

    /// <summary>
    /// Verifies that the background loop picks up a change made elsewhere without any request.
    /// </summary>
    [Fact]
    public async Task TheBackgroundLoop_PicksUpAChangeMadeElsewhere_WithoutAnyRequest()
    {
        var settings = SettingsWithTtl(TimeSpan.FromMilliseconds(30));
        await settings.InitializeAsync();
        using var stop = new CancellationTokenSource();
        var loop = settings.RunRefreshLoopAsync(stop.Token);

        await _store.SetAsync("Limits:AskEnabled", "false", "other-server-or-sql", _clock.Now);
        await WaitUntil(() => !settings.Limits.AskEnabled);

        Assert.False(settings.Limits.AskEnabled);
        stop.Cancel();
        await loop;
    }

    /// <summary>
    /// Verifies that the refresh loop stops cleanly when the application stops.
    /// </summary>
    [Fact]
    public async Task TheLoop_StopsCleanly_WhenTheApplicationStops()
    {
        var settings = SettingsWithTtl(TimeSpan.FromMilliseconds(10));
        using var stop = new CancellationTokenSource();
        var loop = settings.RunRefreshLoopAsync(stop.Token);

        stop.Cancel();

        await loop.WaitAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Verifies that when saving to the store fails, nothing changes and the user gets a clear error.
    /// </summary>
    [Fact]
    public async Task WhenTheStoreFailsOnSave_NothingChanges_AndTheUserGetsAClearError()
    {
        var settings = Settings();
        _store.Broken = true;

        var result = await settings.SetAsync("a", "Limits:QuestionsPerMinute", "7");

        Assert.Equal(AccessError.Unavailable, result.Error);
        Assert.Equal(20, settings.Limits.QuestionsPerMinute);
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that runtime limits read the live value on every access.
    /// </summary>
    [Fact]
    public async Task RuntimeLimits_ReadsTheLiveValueOnEveryAccess()
    {
        var settings = Settings();
        ILimitsSource limits = new RuntimeLimits(settings);

        await settings.SetAsync("a", "Limits:MaxQuestionChars", "300");

        Assert.Equal(300, limits.Current.MaxQuestionChars);
    }

    /// <summary>
    /// Verifies that GetStates lists every catalog setting with its default.
    /// </summary>
    [Fact]
    public void GetStates_ListsEveryCatalogSetting_WithItsDefault()
    {
        var states = Settings().GetStates();

        Assert.Equal(SettingsCatalog.All.Count, states.Count);
        Assert.Equal("20", states.Single(s => s.Definition.Key == "Limits:QuestionsPerMinute").Default);
        Assert.All(states, s => Assert.Equal("default", s.Source));
    }
}
