using System.Security.Cryptography;
using System.Text;

namespace WriteUp.Services;

internal static class SpeechCredential
{
    public static string Encrypt(string key) => string.IsNullOrWhiteSpace(key) ? "" :
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    public static string Decrypt(string encrypted) => string.IsNullOrWhiteSpace(encrypted) ? "" :
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
}
