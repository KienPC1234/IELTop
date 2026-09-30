using Microsoft.Extensions.Caching.Memory;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Rate limits side effect writes that would otherwise happen on every
/// request: bumping a code's use count, an account's last seen time, or a
/// download counter. Ten thousand downloads should not mean ten thousand
/// updates on the hot path.
///
/// The window is deliberately coarse. These numbers are for a dashboard,
/// not for billing, so a few missed increments under load are fine.
/// </summary>
public interface IWriteThrottle
{
    /// <summary>
    /// True at most once per window per key, and then only when called
    /// again after the window. Safe to call from many requests at once.
    /// </summary>
    bool ShouldWrite(string key, TimeSpan window);
}

public sealed class WriteThrottle(IMemoryCache cache) : IWriteThrottle
{
    public bool ShouldWrite(string key, TimeSpan window)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        string full = "w:" + key;
        if (cache.TryGetValue(full, out _))
            return false;

        // A small race here can let two writers through within the same
        // instant. That is harmless, the value is a counter.
        cache.Set(full, true, window);
        return true;
    }
}
