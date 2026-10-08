using System;
using System.Security.Cryptography;

namespace LMS.Identity.BusinessSerive.Common
{
    /// <summary>
    /// PBKDF2 (HMAC-SHA256) password hashing with support for verifying legacy
    /// plaintext passwords so existing rows keep working and can be upgraded in place.
    /// </summary>
    using System;
    using System.Security.Cryptography;

    public static class PasswordHasher
    {
        private const string HashPrefix = "PBKDF2$";
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;

        public static string Hash(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(KeySize);

            return $"{HashPrefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static bool IsHashed(string storedPassword)
        {
            return !string.IsNullOrWhiteSpace(storedPassword)
                   && storedPassword.StartsWith(HashPrefix, StringComparison.Ordinal);
        }

        public static bool Verify(string storedPassword, string suppliedPassword, out bool needsUpgrade)
        {
            needsUpgrade = false;

            if (string.IsNullOrEmpty(storedPassword) || suppliedPassword == null)
            {
                return false;
            }

            if (!IsHashed(storedPassword))
            {
                var matched = string.Equals(storedPassword, suppliedPassword, StringComparison.Ordinal);
                needsUpgrade = matched;
                return matched;
            }

            var parts = storedPassword.Substring(HashPrefix.Length).Split('$');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out var iterations)
                || iterations <= 0)
            {
                return false;
            }

            byte[] salt;
            byte[] expectedKey;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expectedKey = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            // Try current algorithm (SHA-256) first.
            if (VerifyWithAlgorithm(suppliedPassword, salt, iterations, expectedKey, HashAlgorithmName.SHA256))
            {
                return true;
            }

            // Legacy fallback: PBKDF2 with SHA-1 (older Rfc2898DeriveBytes default).
            if (VerifyWithAlgorithm(suppliedPassword, salt, iterations, expectedKey, HashAlgorithmName.SHA1))
            {
                needsUpgrade = true; // caller should persist new SHA-256 hash
                return true;
            }

            return false;
        }

        public static bool Verify(string storedPassword, string suppliedPassword)
        {
            return Verify(storedPassword, suppliedPassword, out _);
        }

        private static bool VerifyWithAlgorithm(string password, byte[] salt, int iterations, byte[] expectedKey, HashAlgorithmName algorithm)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, algorithm);
            var actualKey = pbkdf2.GetBytes(expectedKey.Length);
            return CryptographicOperations.FixedTimeEquals(expectedKey, actualKey);
        }
    }
}
