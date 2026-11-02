using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.Setup.Abstractions;

namespace CrmNlQuery.Application.Setup.Services;

/// <summary>
/// Encrypts settings with AES-256-GCM; each value gets its own random nonce and carries an authentication tag, so any change is detected.
/// </summary>
public sealed class AesGcmConfigCipher : IConfigCipher
{
    public const int KeyBytes = 32;
    private const string Prefix = "v1:";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public AesGcmConfigCipher(byte[] key)
    {
        if (key is null || key.Length != KeyBytes)
        {
            throw new ArgumentException($"The key must be {KeyBytes} bytes.", nameof(key));
        }
        _key = (byte[])key.Clone();
    }

    /// <summary>
    /// Encrypts the text into v1:base64(nonce + tag + cipher).
    /// </summary>
    public string Encrypt(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText ?? "");
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    /// <summary>
    /// Decrypts a v1 value, throwing CryptographicException when it is malformed, changed or made with another key.
    /// </summary>
    public string Decrypt(string cipherText)
    {
        if (cipherText is null || !cipherText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The stored value is not an encrypted setting.");
        }
        byte[] data;
        try
        {
            data = Convert.FromBase64String(cipherText.Substring(Prefix.Length));
        }
        catch (FormatException)
        {
            throw new CryptographicException("The stored value is not valid.");
        }
        if (data.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("The stored value is too short.");
        }
        var nonce = data.AsSpan(0, NonceBytes);
        var tag = data.AsSpan(NonceBytes, TagBytes);
        var cipher = data.AsSpan(NonceBytes + TagBytes);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
