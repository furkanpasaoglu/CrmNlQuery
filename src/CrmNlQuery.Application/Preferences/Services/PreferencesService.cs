using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Preferences.Abstractions;

namespace CrmNlQuery.Application.Preferences.Services;

/// <summary>
/// Stores how users want to be addressed and their language preference; the display name is used only in the widget greeting and never goes to the model, logs or reports.
/// </summary>
public sealed class PreferencesService
{
    public const int MaxNameLength = 30;

    private readonly IPreferencesStore _store;

    public PreferencesService(IPreferencesStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Returns the owner's display name, or null.
    /// </summary>
    public Task<string?> GetDisplayNameAsync(Guid owner, CancellationToken ct = default)
    {
        return _store.GetDisplayNameAsync(owner, ct);
    }

    /// <summary>
    /// Saves the cleaned display name and returns it, or deletes it and returns null for empty input.
    /// </summary>
    public async Task<string?> SetDisplayNameAsync(Guid owner, string? displayName, CancellationToken ct = default)
    {
        var clean = CleanName(displayName);
        var value = clean.Length == 0 ? null : clean;
        await _store.SetDisplayNameAsync(owner, value, ct);
        return value;
    }

    /// <summary>
    /// Returns the language preference bound to the account.
    /// </summary>
    public async Task<string?> GetLanguageAsync(string account, CancellationToken ct = default)
    {
        return CrmNlQuery.Domain.Common.Constants.UiLanguage.Parse(await _store.GetLanguageAsync(NormalizeAccount(account), ct));
    }

    /// <summary>
    /// Saves a supported language for the account, or clears the preference for empty input.
    /// </summary>
    public async Task<(bool Ok, string? Saved)> SetLanguageAsync(string account, string? language, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            await _store.SetLanguageAsync(NormalizeAccount(account), null, ct);
            return (true, null);
        }
        var parsed = CrmNlQuery.Domain.Common.Constants.UiLanguage.Parse(language);
        if (parsed is null)
        {
            return (false, null);
        }
        await _store.SetLanguageAsync(NormalizeAccount(account), parsed, ct);
        return (true, parsed);
    }

    private static string NormalizeAccount(string account) => (account ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Cleans a display name by trimming and limiting it.
    /// </summary>
    public static string CleanName(string? name) => PromptText.Clean(name, MaxNameLength);
}
