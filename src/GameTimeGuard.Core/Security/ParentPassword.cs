using System.Security.Cryptography;
using System.Text;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Security;

public static class ParentPassword
{
    public const int DefaultIterations = 600_000;
    public const int SaltBytes = 16;
    public const int HashBytes = 32;
    public const int MinimumLength = 4;
    public const int MaximumLength = 128;

    public static PasswordHash Hash(string password, int? iterations = null)
    {
        ValidateChoice(password);
        var rounds = iterations ?? DefaultIterations;
        if (rounds is < 10_000 or > 2_000_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "迭代次数超出允许范围");

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, HashBytes);
        return new PasswordHash
        {
            Algorithm = "PBKDF2-SHA256",
            Iterations = rounds,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash)
        };
    }

    public static bool Verify(string? password, PasswordHash? stored)
    {
        if (stored is null || string.IsNullOrEmpty(password) || password.Length > MaximumLength)
            return false;
        if (!stored.Algorithm.Equals("PBKDF2-SHA256", StringComparison.Ordinal))
            return false;
        if (stored.Iterations is < 10_000 or > 2_000_000)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(stored.Salt);
            expected = Convert.FromBase64String(stored.Hash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length < 8 || expected.Length < 16)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, stored.Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static void ValidateChoice(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            throw new ArgumentException("家长密码至少 4 位");
        if (password.Length > MaximumLength)
            throw new ArgumentException("家长密码不能超过 128 位");
        if (password.Contains('\r') || password.Contains('\n') || password.Contains('\0'))
            throw new ArgumentException("家长密码不能包含换行");
    }
}
