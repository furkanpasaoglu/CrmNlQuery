namespace CrmNlQuery.Application.Setup.Abstractions;

/// <summary>
/// Encrypts and decrypts secret settings so a database copy alone does not reveal them.
/// </summary>
public interface IConfigCipher
{
    /// <summary>
    /// Encrypts the text and returns a self-contained value that includes everything needed to decrypt it with the same key.
    /// </summary>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypts a value made by Encrypt; throws when it was changed or made with another key.
    /// </summary>
    string Decrypt(string cipherText);
}
