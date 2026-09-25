using System.Security.Cryptography;
using System.Text;

namespace IELTop.Services.Storage;

/// <summary>
/// Protects secrets at rest with Windows DPAPI, per user.
/// On non-Windows or failure it falls back to base64 so the app still runs,
/// but the value is not exposed as plain text in the file.
/// </summary>
public static class SecretProtector
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(protectedBytes);
        }
        catch (CryptographicException)
        {
            return "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }
        catch (PlatformNotSupportedException)
        {
            return "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        try
        {
            var payload = stored.Contains(':') ? stored[(stored.IndexOf(':') + 1)..] : stored;
            var bytes = Convert.FromBase64String(payload);

            if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                var plainBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return string.Empty;
        }
        catch (CryptographicException)
        {
            // Key was written by another user account, so it cannot be read here.
            return string.Empty;
        }
    }
}
