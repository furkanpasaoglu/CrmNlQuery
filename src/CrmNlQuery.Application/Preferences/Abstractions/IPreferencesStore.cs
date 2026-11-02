namespace CrmNlQuery.Application.Preferences.Abstractions;

/// <summary>
/// Defines per-user small preferences such as display name and language, each tied to its owner so nobody reads another's.
/// </summary>
public interface IPreferencesStore
{
    /// <summary>
    /// Returns the saved display name, or null if none or deleted.
    /// </summary>
    Task<string?> GetDisplayNameAsync(Guid owner, CancellationToken ct = default);

    /// <summary>
    /// Saves a user's display name, where null deletes it.
    /// </summary>
    Task SetDisplayNameAsync(Guid owner, string? displayName, CancellationToken ct = default);

    /// <summary>
    /// Returns the UI language preference for an account, independent of CRM identity so the panel and widget share it.
    /// </summary>
    Task<string?> GetLanguageAsync(string account, CancellationToken ct = default);

    /// <summary>
    /// Saves or clears the UI language preference for an account.
    /// </summary>
    Task SetLanguageAsync(string account, string? language, CancellationToken ct = default);
}
