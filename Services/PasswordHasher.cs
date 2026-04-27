using System.Security.Cryptography;

namespace DiscordCloneServer.Services
{
    public enum PasswordVerificationResult
    {
        Failed,
        Success,
        SuccessRehashNeeded
    }

    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;
        private const string FormatPrefix = "pbkdf2_sha256";

        public static string HashPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password is required.", nameof(password));
            }

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize
            );

            return string.Join(
                "$",
                FormatPrefix,
                Iterations,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash)
            );
        }

        public static PasswordVerificationResult VerifyPassword(string storedPassword, string suppliedPassword)
        {
            if (string.IsNullOrEmpty(storedPassword) || string.IsNullOrEmpty(suppliedPassword))
            {
                return PasswordVerificationResult.Failed;
            }

            if (!storedPassword.StartsWith($"{FormatPrefix}$", StringComparison.Ordinal))
            {
                return SlowEquals(storedPassword, suppliedPassword)
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Failed;
            }

            var parts = storedPassword.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
            {
                return PasswordVerificationResult.Failed;
            }

            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expectedHash = Convert.FromBase64String(parts[3]);
                var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    suppliedPassword,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length
                );

                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                {
                    return PasswordVerificationResult.Failed;
                }

                return iterations < Iterations
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Success;
            }
            catch (FormatException)
            {
                return PasswordVerificationResult.Failed;
            }
        }

        private static bool SlowEquals(string a, string b)
        {
            var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
            var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
