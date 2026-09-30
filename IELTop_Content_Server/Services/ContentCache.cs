using System.Collections.Concurrent;
using System.Text.Json;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Two layer cache in front of the database. L1 is in process memory,
/// L2 is the distributed cache (Redis in production, memory locally).
/// Keys carry a content version, so one bump makes every derived key
/// stale at once without scanning the cache.
/// </summary>
public interface IContentCache
{
    Task<long> VersionAsync(CancellationToken ct = default);
    Task BumpVersionAsync(CancellationToken ct = default);

    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);
    Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);
    Task<string> GetOrAddStringAsync(string key, TimeSpan ttl, Func<CancellationToken, Task<string>> factory, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public sealed class ContentCache(
    IMemoryCache memory,
    IDistributedCache distributed,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<CacheOptions> options) : IContentCache
{
    private const string VersionKey = "content:version";
    private readonly CacheOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan VersionTtl = TimeSpan.FromSeconds(30);

    public async Task<long> VersionAsync(CancellationToken ct = default)
    {
        if (memory.TryGetValue<long>(VersionKey, out var cached))
            return cached;

        long version = await ReadVersionAsync(ct);
        memory.Set(VersionKey, version, VersionTtl);
        return version;
    }

    private async Task<long> ReadVersionAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var row = await db.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "content.version", ct);
            return row is null || !long.TryParse(row.Value, out var value) ? 1 : value;
        }
        catch (Exception)
        {
            // A store that cannot be read still serves the seeded copy.
            return 1;
        }
    }

    public async Task BumpVersionAsync(CancellationToken ct = default)
    {
        long next;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == "content.version", ct);
            if (row is null)
            {
                row = new Setting { Key = "content.version", Value = "2" };
                db.Settings.Add(row);
            }
            else
            {
                long.TryParse(row.Value, out var current);
                row.Value = (current + 1).ToString();
            }
            next = long.Parse(row.Value);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            next = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            memory.Set(VersionKey, next, VersionTtl);
            return;
        }

        memory.Set(VersionKey, next, VersionTtl);
        // Drop the paper payloads so a stale detail page cannot outlive
        // a fresh list. Keys are versioned, so this is belt and braces.
        await SafeRemoveAsync($"{VersionKey}:detail:{next - 1}");
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (memory.TryGetValue<T>(key, out var local) && local is not null)
            return local;

        try
        {
            byte[]? bytes = await distributed.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0)
                return default;
            var value = JsonSerializer.Deserialize<T>(bytes, Json);
            if (value is not null)
                memory.Set(key, value, MemoryTtl());
            return value;
        }
        catch (Exception)
        {
            // A cache miss is always safe to fall back from.
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        memory.Set(key, value, ttl);
        try
        {
            await distributed.SetAsync(
                key,
                JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);
        }
        catch (Exception)
        {
            // L1 already holds the value.
        }
    }

    public async Task<T> GetOrAddAsync<T>(
        string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        var existing = await GetAsync<T>(key, ct);
        if (existing is not null)
            return existing;

        // One builder per key, so a stampede of clients builds once.
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            existing = await GetAsync<T>(key, ct);
            if (existing is not null)
                return existing;

            var value = await factory(ct);
            if (value is not null)
                await SetAsync(key, value, ttl, ct);
            return value;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
                _gates.TryRemove(key, out _);
        }
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        if (memory.TryGetValue<string>(key, out var local) && local is not null)
            return local;

        try
        {
            var value = await distributed.GetStringAsync(key, ct);
            if (value is not null)
                memory.Set(key, value, MemoryTtl());
            return value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        memory.Set(key, value, ttl);
        try
        {
            await distributed.SetStringAsync(
                key, value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);
        }
        catch (Exception)
        {
            // L1 already holds the value.
        }
    }

    public async Task<string> GetOrAddStringAsync(
        string key, TimeSpan ttl, Func<CancellationToken, Task<string>> factory, CancellationToken ct = default)
    {
        var existing = await GetStringAsync(key, ct);
        if (existing is not null)
            return existing;

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            existing = await GetStringAsync(key, ct);
            if (existing is not null)
                return existing;

            var value = await factory(ct);
            if (value is not null)
                await SetStringAsync(key, value, ttl, ct);
            return value ?? string.Empty;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
                _gates.TryRemove(key, out _);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        memory.Remove(key);
        await SafeRemoveAsync(key, ct);
    }

    private async Task SafeRemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await distributed.RemoveAsync(key, ct);
        }
        catch (Exception)
        {
            // Ignore, the entry expires on its own.
        }
    }

    private TimeSpan MemoryTtl() =>
        TimeSpan.FromSeconds(Math.Max(5, Math.Min(_options.PaperListSeconds, 60)));
}
