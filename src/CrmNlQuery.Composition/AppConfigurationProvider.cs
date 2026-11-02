using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Application.Setup.Services;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Composition;

/// <summary>
/// Configuration provider for the setup wizard settings: it asks a reader for the stored settings and decrypts the secrets; nothing is provided while the settings cannot be read, and the next reload tries again.
/// </summary>
public sealed class AppConfigurationProvider : ConfigurationProvider
{
    private readonly Func<IReadOnlyList<AppConfigEntry>> _read;
    private readonly Func<IConfigCipher?> _cipher;

    public AppConfigurationProvider(Func<IReadOnlyList<AppConfigEntry>> read, Func<IConfigCipher?> cipher)
    {
        _read = read;
        _cipher = cipher;
    }

    /// <summary>
    /// Reads the stored settings and makes them available as configuration values; secrets that cannot be decrypted are left out and listed under Setup:UndecryptableKeys.
    /// </summary>
    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var entries = _read();
        var skipped = new List<string>();
        foreach (var pair in AppConfigMapper.ToConfiguration(entries, entries.Any(e => e.Secret) ? SafeCipher() : null, skipped))
        {
            Data[pair.Key] = pair.Value;
        }
        if (skipped.Count > 0)
        {
            Data["Setup:UndecryptableKeys"] = string.Join(",", skipped);
        }
    }

    private IConfigCipher? SafeCipher()
    {
        try { return _cipher(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }
}
