using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace IELTop_Content_Server;

/// <summary>
/// Bucket keys for the rate limiter, plus a small login attempt tracker.
///
/// A caller that presents a credential we have already accepted gets its
/// own bucket, so one busy class behind a shared address cannot starve
/// another. A made up code or token does not get its own bucket, it falls
/// back to the address bucket, so a flood of random values cannot grow
/// the limiter table without bound.
/// </summary>
public static class RateLimitKeys
{
    // Hashes of credentials that authenticated at least once. Capped so a
    // flood of distinct values can never grow this without limit.
    private static readonly ConcurrentDictionary<string, byte> Known = new();
    private static int _capacity = 20_000;

    public static void Configure(int capacity) =>
        _capacity = Math.Clamp(capacity, 100, 1_000_000);

    /// <summary>Seed for the current request, or empty when anonymous.</summary>
    public static string Seed(HttpContext context)
    {
        string code = context.Request.Headers["X-Access-Code"].FirstOrDefault() ?? string.Empty;
        if (code.Length > 0)
            return "code:" + code;

        string auth = context.Request.Headers.Authorization.FirstOrDefault() ?? string.Empty;
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return "token:" + auth[7..].Trim();

        return string.Empty;
    }

    /// <summary>
    /// The bucket key for a protocol request. A known credential gets a
    /// shared bucket by credential, everyone else by address.
    /// </summary>
    public static string ProtocolIdentity(HttpContext context)
    {
        string seed = Seed(context);
        if (seed.Length == 0)
            return Address(context);

        string hash = Hash(seed);
        return Known.ContainsKey(hash) ? "key:" + hash : Address(context);
    }

    public static string Address(HttpContext context) =>
        "ip:" + context.GetClientIp();

    /// <summary>
    /// Called after a credential is accepted, so the next request from it
    /// gets its own bucket. Bounded: when full, the oldest half is dropped.
    /// </summary>
    public static void Remember(string seed)
    {
        if (string.IsNullOrEmpty(seed))
            return;

        string hash = Hash(seed);
        if (Known.ContainsKey(hash))
            return;

        if (Known.Count >= _capacity)
        {
            // Cheap eviction. A limiter bucket is only a rate hint, so
            // losing some is harmless, and this keeps memory flat.
            int drop = Math.Max(1, Known.Count / 2);
            foreach (var key in Known.Keys.Take(drop).ToList())
                Known.TryRemove(key, out _);
        }

        Known.TryAdd(hash, 0);
    }

    /// <summary>
    /// Remembers the credential of an authenticated protocol request, so
    /// the caller graduates from the address bucket to its own.
    /// </summary>
    public static void RememberFromRequest(HttpContext context) =>
        Remember(Seed(context));

    private static string Hash(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..24];
}
