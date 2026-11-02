namespace CrmNlQuery.Application.Operations.Abstractions;

/// <summary>
/// Stores panel setting overrides as key and value pairs.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Returns all stored runtime setting overrides as a key-to-value dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    /// <summary>
    /// Saves a setting override with the actor and time.
    /// </summary>
    Task SetAsync(string key, string value, string actor, DateTimeOffset at, CancellationToken ct = default);
    /// <summary>
    /// Removes one stored runtime setting override by key, so the setting falls back to file or default.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
