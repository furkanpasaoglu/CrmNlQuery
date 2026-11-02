using System.Security.Cryptography;
using CrmNlQuery.Application.Setup.Abstractions;

namespace CrmNlQuery.Application.Setup.Services;

/// <summary>
/// Reads the encryption key for stored settings from a file on the data volume, creating a random one the first time and keeping the file private to the service.
/// </summary>
public static class ConfigKeyFile
{
    /// <summary>
    /// Returns the cipher for the key in the file, or null when the file does not exist and creation was not asked for.
    /// </summary>
    public static IConfigCipher? Open(string path, bool createIfMissing)
    {
        if (!File.Exists(path))
        {
            if (!createIfMissing)
            {
                return null;
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var key = RandomNumberGenerator.GetBytes(AesGcmConfigCipher.KeyBytes);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(key, 0, key.Length);
            }
            RestrictToOwner(path);
        }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != AesGcmConfigCipher.KeyBytes)
        {
            throw new InvalidOperationException("The configuration key file is damaged: " + path);
        }
        return new AesGcmConfigCipher(bytes);
    }

    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
