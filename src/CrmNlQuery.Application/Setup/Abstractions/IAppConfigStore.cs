using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Application.Setup.Abstractions;

/// <summary>
/// Keeps the settings chosen in the setup wizard; secrets are stored already encrypted.
/// </summary>
public interface IAppConfigStore
{
    /// <summary>
    /// Returns every stored setting.
    /// </summary>
    Task<IReadOnlyList<AppConfigEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds or replaces the given settings in one step.
    /// </summary>
    Task SetManyAsync(IReadOnlyList<AppConfigEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Removes every stored setting, used when the setup is reset.
    /// </summary>
    Task DeleteAllAsync(CancellationToken ct = default);
}
