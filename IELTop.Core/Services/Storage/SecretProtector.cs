using System.Security.Cryptography;
using System.Text;

namespace IELTop.Services.Storage;

/// <summary>
/// Protects secrets at rest without tying the app to one OS. The key is a
/// random 32 byte file created once in the data folder, so the same value
/// decrypts on Windows, macOS and Linux. Files written by the old Windows
/// build used DPAPI; those are detected and treated as unreadable, so the
/// user simply enters the key again rather than seeing a crash.
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "aes1:";
    private const string KeyFileName = "secret.key";

    private static readonly object Gate = new();
    private static byte[]? _cachedKey;

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        try
        {
            var key = LoadKey();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plainBytes = Encoding.UTF8.GetBytes(plain);
            var cipher = new byte[plainBytes.Length];
            var tag = new byte[16];

            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plainBytes, cipher, tag);

            var payload = new byte[nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
            return Prefix + Convert.ToBase64String(payload);
        }
        catch (Exception)
        {
            // Never write a key as plain text; an empty value just asks again.
            return string.Empty;
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;

        // A key saved by the old DPAPI build cannot be read here.
        if (stored.StartsWith("dpapi:", StringComparison.Ordinal)) return string.Empty;

        if (stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            try
            {
                var key = LoadKey();
                var payload = Convert.FromBase64String(stored[Prefix.Length..]);
                if (payload.Length < 28) return string.Empty;

                var nonce = payload.AsSpan(0, 12);
                var tag = payload.AsSpan(12, 16);
                var cipher = payload.AsSpan(28);
                var plain = new byte[cipher.Length];

                using var aes = new AesGcm(key, tag.Length);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // Legacy base64 only values from very old builds.
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static byte[] LoadKey()
    {
        if (_cachedKey is not null) return _cachedKey;

        lock (Gate)
        {
            if (_cachedKey is not null) return _cachedKey;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IELTop");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, KeyFileName);

            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32)
                {
                    _cachedKey = existing;
                    return _cachedKey;
                }
            }

            var key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(path, key);
            TryRestrict(path);
            _cachedKey = key;
            return _cachedKey;
        }
    }

    /// <summary>Best effort file permission tightening; ignores platforms that refuse.</summary>
    private static void TryRestrict(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return; // NTFS inherits the user profile ACL, which is already private.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Nothing to do; the file still lives in the per user folder.
        }
    }
}
