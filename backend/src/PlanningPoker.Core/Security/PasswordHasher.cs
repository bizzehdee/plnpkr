using System.Security.Cryptography;

namespace PlanningPoker.Core.Security;

/// <summary>
/// Hashes and verifies an optional session password. Implementations use a strong, salted, slow KDF;
/// the plaintext is never stored. See #2.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Returns an encoded hash string (algorithm + params + salt + hash) for storage.</summary>
    string Hash(string password);

    /// <summary>Verifies a candidate password against a stored encoded hash, in constant time.</summary>
    bool Verify(string encodedHash, string password);
}

/// <summary>
/// PBKDF2-HMAC-SHA256 password hasher (per-password salt, high iteration count). Encoded format is
/// <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 subkey&gt;</c>. Lives in Core with no
/// extra dependency (uses <see cref="Rfc2898DeriveBytes"/>). See #2.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;       // 128-bit salt
    private const int KeySize = 32;        // 256-bit derived key
    private const int Iterations = 100_000; // OWASP-recommended floor for PBKDF2-HMAC-SHA256
    private const string Prefix = "pbkdf2-sha256";

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
    }

    public bool Verify(string encodedHash, string password)
    {
        if (string.IsNullOrEmpty(encodedHash) || password is null)
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
