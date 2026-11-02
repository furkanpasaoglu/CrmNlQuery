using System.Security.Cryptography;
using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Application.Setup.Services;

/// <summary>
/// Turns stored settings into the key and value pairs the configuration system reads, decrypting secrets and skipping those that cannot be decrypted.
/// </summary>
public static class AppConfigMapper
{
    /// <summary>
    /// Returns the settings by key; secrets that cannot be decrypted (no key, changed or another key) are left out and their keys added to the skipped list.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ToConfiguration(IEnumerable<AppConfigEntry> entries, IConfigCipher? cipher, ICollection<string> skippedSecrets)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!entry.Secret)
            {
                result[entry.Key] = entry.Value;
                continue;
            }
            try
            {
                if (cipher is null)
                {
                    throw new CryptographicException("No key.");
                }
                result[entry.Key] = cipher.Decrypt(entry.Value);
            }
            catch (CryptographicException)
            {
                skippedSecrets.Add(entry.Key);
            }
        }
        return result;
    }
}
