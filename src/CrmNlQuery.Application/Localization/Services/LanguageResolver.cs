using CrmNlQuery.Application.Preferences.Services;
using CrmNlQuery.Domain.Common.Constants;

namespace CrmNlQuery.Application.Localization.Services;

/// <summary>
/// Resolves a request's interface language from the user's saved preference, then Accept-Language, then the admin default.
/// </summary>
public sealed class LanguageResolver
{
    private readonly PreferencesService _preferences;
    private readonly Func<string> _defaultLanguage;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<string, (string? Value, DateTimeOffset Until)> _cache = new Dictionary<string, (string?, DateTimeOffset)>(StringComparer.Ordinal);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public LanguageResolver(PreferencesService preferences, Func<string>? defaultLanguage = null, Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _preferences = preferences;
        _defaultLanguage = defaultLanguage ?? (() => UiLanguage.Default);
    }

    /// <summary>
    /// Resolves the language for an account, Accept-Language header and defaults.
    /// </summary>
    public async Task<string> ResolveAsync(string? account, string? acceptLanguage, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(account))
        {
            try
            {
                var saved = await SavedAsync(account, ct);
                if (saved is not null)
                {
                    return saved;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }
        return UiLanguage.FromAcceptLanguage(acceptLanguage) ?? UiLanguage.Parse(_defaultLanguage()) ?? UiLanguage.Default;
    }

    private async Task<string?> SavedAsync(string account, CancellationToken ct)
    {
        var key = account.Trim().ToLowerInvariant();
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && hit.Until > _clock())
            {
                return hit.Value;
            }
        }
        var value = UiLanguage.Parse(await _preferences.GetLanguageAsync(account, ct));
        lock (_gate)
        {
            _cache[key] = (value, _clock() + CacheFor);
        }
        return value;
    }

    /// <summary>
    /// Clears the cached preference for an account so a change takes effect immediately.
    /// </summary>
    public void Invalidate(string account)
    {
        lock (_gate)
        {
            _cache.Remove((account ?? "").Trim().ToLowerInvariant());
        }
    }

    public string DefaultLanguage => UiLanguage.Parse(_defaultLanguage()) ?? UiLanguage.Default;
}
