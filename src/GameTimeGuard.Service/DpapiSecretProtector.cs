using System.Security.Cryptography;
using System.Text;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Service;

internal sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "GameTimeGuard.Smtp.v1"u8.ToArray();

    public string Protect(string plaintext)
    {
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    public bool TryUnprotect(string protectedText, out string plaintext)
    {
        plaintext = "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.LocalMachine);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
