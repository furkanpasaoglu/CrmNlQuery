using System.Security.Cryptography;
using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Application.Setup.Services;

namespace CrmNlQuery.Tests.Application.Setup.Services;

/// <summary>
/// Tests the encryption of stored settings, the key file and how stored settings become configuration values.
/// </summary>
public class ConfigCipherTests
{
    private static AesGcmConfigCipher Cipher(byte fill = 7) => new AesGcmConfigCipher(Enumerable.Repeat(fill, AesGcmConfigCipher.KeyBytes).ToArray());

    [Fact]
    public void AValueRoundTripsAndNeverShowsThePlainText()
    {
        var cipher = Cipher();

        var stored = cipher.Encrypt("Şifre-123!");

        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain("Şifre", stored);
        Assert.Equal("Şifre-123!", cipher.Decrypt(stored));
    }

    [Fact]
    public void TheSameValueEncryptsDifferentlyEachTime()
    {
        var cipher = Cipher();

        Assert.NotEqual(cipher.Encrypt("same"), cipher.Encrypt("same"));
    }

    [Fact]
    public void AChangedValueOrAnotherKeyOrGarbageCannotBeDecrypted()
    {
        var stored = Cipher().Encrypt("secret");
        var raw = Convert.FromBase64String(stored.Substring(3));
        raw[raw.Length - 1] ^= 1;
        var changed = "v1:" + Convert.ToBase64String(raw);

        Assert.ThrowsAny<CryptographicException>(() => Cipher().Decrypt(changed));
        Assert.ThrowsAny<CryptographicException>(() => Cipher(9).Decrypt(stored));
        Assert.ThrowsAny<CryptographicException>(() => Cipher().Decrypt("plain text"));
        Assert.ThrowsAny<CryptographicException>(() => Cipher().Decrypt("v1:not-base64!!"));
        Assert.ThrowsAny<CryptographicException>(() => Cipher().Decrypt("v1:AAAA"));
    }

    [Fact]
    public void AWrongSizedKeyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmConfigCipher(new byte[16]));
    }

    [Fact]
    public void TheKeyFileIsCreatedOnceAndTheSameKeyIsUsedAfterwards()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nlq-key-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "config.key");
        try
        {
            Assert.Null(ConfigKeyFile.Open(path, createIfMissing: false));
            var first = ConfigKeyFile.Open(path, createIfMissing: true)!;
            var stored = first.Encrypt("value");
            var second = ConfigKeyFile.Open(path, createIfMissing: false)!;

            Assert.Equal("value", second.Decrypt(stored));
            Assert.Equal(AesGcmConfigCipher.KeyBytes, new FileInfo(path).Length);
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
        }
    }

    [Fact]
    public void ADamagedKeyFileIsReportedInsteadOfBeingReplaced()
    {
        var path = Path.Combine(Path.GetTempPath(), "nlq-key-" + Guid.NewGuid().ToString("N") + ".key");
        try
        {
            File.WriteAllBytes(path, new byte[5]);

            Assert.Throws<InvalidOperationException>(() => ConfigKeyFile.Open(path, createIfMissing: true));
            Assert.Equal(5, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MapperDecryptsSecretsAndSkipsTheOnesItCannotRead()
    {
        var cipher = Cipher();
        var entries = new[]
        {
            new AppConfigEntry("Crm:BaseUrl", "https://crm.example/org", false),
            new AppConfigEntry("Crm:Password", cipher.Encrypt("p@ss"), true),
            new AppConfigEntry("Anthropic:ApiKey", Cipher(9).Encrypt("other key"), true),
        };
        var skipped = new List<string>();

        var map = AppConfigMapper.ToConfiguration(entries, cipher, skipped);

        Assert.Equal("https://crm.example/org", map["Crm:BaseUrl"]);
        Assert.Equal("p@ss", map["Crm:Password"]);
        Assert.False(map.ContainsKey("Anthropic:ApiKey"));
        Assert.Equal(new[] { "Anthropic:ApiKey" }, skipped);
    }

    [Fact]
    public void WithoutAKeyEverySecretIsSkippedButPlainSettingsStay()
    {
        var entries = new[] { new AppConfigEntry("A", "1", false), new AppConfigEntry("B", Cipher().Encrypt("2"), true) };
        var skipped = new List<string>();

        var map = AppConfigMapper.ToConfiguration(entries, null, skipped);

        Assert.Equal("1", map["A"]);
        Assert.Equal(new[] { "B" }, skipped);
    }
}
