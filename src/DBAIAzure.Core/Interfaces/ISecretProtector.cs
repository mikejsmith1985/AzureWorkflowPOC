// Encryption seam for connector secret values stored at rest (FR-019, Article IX).
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Encrypts and decrypts connector secret values so they are never persisted in plain text.
/// The implementation lives in the web host (ASP.NET Core Data Protection); callers in the
/// storage layer depend only on this interface, not on any ASP.NET Core type.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/>. Returns an opaque ciphertext string safe to store.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a ciphertext value produced by <see cref="Protect"/>.
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> when the payload is
    /// tampered with or the underlying key ring has been rotated since protection.
    /// </summary>
    string Unprotect(string ciphertext);
}
