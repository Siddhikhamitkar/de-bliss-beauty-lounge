using System.Security.Cryptography;
using System.Text;

namespace DeBliss.Application.Identity;

/// <summary>
/// Generation and hashing of one-time codes and verification tokens. Codes are
/// stored as salted SHA-256 (they live minutes and allow a handful of guesses);
/// tokens are 256-bit random values stored as plain SHA-256, which is enough for
/// a secret that cannot be guessed.
/// </summary>
public static class VerificationSecrets
{
    public static string NewCode(int length)
    {
        var builder = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            builder.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
        }

        return builder.ToString();
    }

    public static string HashCode(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(Digest(salt, code))}";
    }

    public static bool CodeMatches(string storedHash, string candidate)
    {
        var parts = storedHash.Split(':');

        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);

            return CryptographicOperations.FixedTimeEquals(expected, Digest(salt, candidate.Trim()));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static byte[] Digest(byte[] salt, string code)
    {
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var buffer = new byte[salt.Length + codeBytes.Length];
        salt.CopyTo(buffer, 0);
        codeBytes.CopyTo(buffer, salt.Length);

        return SHA256.HashData(buffer);
    }
}
