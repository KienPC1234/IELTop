using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Monitors IP behavior, prevents automated abuse, tracks violation strikes,
/// and manages active IP bans.
/// </summary>
public interface IIpAbuseGuard
{
    Task<bool> IsBlockedAsync(string ip, CancellationToken ct = default);
    Task BlockIpAsync(string ip, string reason, TimeSpan? duration, string createdBy = "System", CancellationToken ct = default);
    Task UnblockIpAsync(string ip, string actor, CancellationToken ct = default);
    Task RecordFailedLoginStrikeAsync(string ip, string account, CancellationToken ct = default);
    Task RecordHoneypotTriggerAsync(string ip, string form, CancellationToken ct = default);
    Task RecordSensitiveProbeAsync(string ip, string path, CancellationToken ct = default);
    Task<List<BlockedIp>> ListBlockedIpsAsync(CancellationToken ct = default);
}

public sealed class IpAbuseGuard(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache,
    IAuditService audit,
    ILogger<IpAbuseGuard> logger) : IIpAbuseGuard
{
    private const int MaxStrikesBeforeBan = 5;
    private static readonly TimeSpan StrikeWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LoginBanDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan HoneypotBanDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan ProbeBanDuration = TimeSpan.FromHours(24);

    private static string BlockCacheKey(string ip) => $"abuse:blocked:{ip.Trim()}";
    private static string StrikeKey(string ip) => $"abuse:strikes:{ip.Trim()}";

    public static bool IsInternalOrLoopback(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "unknown")
            return true;
        string clean = ip.Trim();
        if (clean is "127.0.0.1" or "::1" or "localhost")
            return true;
        if (System.Net.IPAddress.TryParse(clean, out var addr))
        {
            if (System.Net.IPAddress.IsLoopback(addr))
                return true;
            if (addr.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(addr.MapToIPv4()))
                return true;
        }
        return false;
    }

    public async Task<bool> IsBlockedAsync(string ip, CancellationToken ct = default)
    {
        if (IsInternalOrLoopback(ip))
            return false;

        string cleanIp = ip.Trim();
        string key = BlockCacheKey(cleanIp);

        if (cache.TryGetValue(key, out bool isBlocked))
            return isBlocked;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var record = await db.BlockedIps.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Ip == cleanIp && (b.ExpiresAt == null || b.ExpiresAt > now), ct);

        if (record is not null)
        {
            var cacheDuration = record.ExpiresAt.HasValue
                ? (record.ExpiresAt.Value - now)
                : TimeSpan.FromMinutes(30);

            if (cacheDuration <= TimeSpan.Zero)
                return false;

            cache.Set(key, true, cacheDuration);
            return true;
        }

        // Cache negative lookup briefly to avoid excessive DB reads
        cache.Set(key, false, TimeSpan.FromSeconds(30));
        return false;
    }

    public async Task BlockIpAsync(
        string ip, string reason, TimeSpan? duration, string createdBy = "System", CancellationToken ct = default)
    {
        if (IsInternalOrLoopback(ip))
            return;

        string cleanIp = ip.Trim();
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = duration.HasValue ? now.Add(duration.Value) : null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.BlockedIps.FirstOrDefaultAsync(b => b.Ip == cleanIp, ct);
        if (existing is not null)
        {
            existing.Reason = reason;
            existing.BlockedAt = now;
            existing.ExpiresAt = expiresAt;
            existing.CreatedBy = createdBy;
        }
        else
        {
            db.BlockedIps.Add(new BlockedIp
            {
                Ip = cleanIp,
                Reason = reason,
                BlockedAt = now,
                ExpiresAt = expiresAt,
                CreatedBy = createdBy
            });
        }

        await db.SaveChangesAsync(ct);

        var cacheDuration = duration ?? TimeSpan.FromHours(24);
        cache.Set(BlockCacheKey(cleanIp), true, cacheDuration);

        string expiryText = duration.HasValue ? $"for {duration.Value.TotalMinutes} minutes" : "indefinitely";
        logger.LogWarning("IP {Ip} blocked by {Creator} {Expiry}: {Reason}", cleanIp, createdBy, expiryText, reason);

        await audit.WriteAsync(createdBy, "security.ip.blocked", cleanIp, $"{reason} ({expiryText})", cleanIp, ct);
    }

    public async Task UnblockIpAsync(string ip, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return;

        string cleanIp = ip.Trim();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var record = await db.BlockedIps.FirstOrDefaultAsync(b => b.Ip == cleanIp, ct);
        if (record is not null)
        {
            db.BlockedIps.Remove(record);
            await db.SaveChangesAsync(ct);
        }

        cache.Remove(BlockCacheKey(cleanIp));
        cache.Remove(StrikeKey(cleanIp));

        logger.LogInformation("IP {Ip} unblocked by {Actor}", cleanIp, actor);
        await audit.WriteAsync(actor, "security.ip.unblocked", cleanIp, "Unblocked by admin", cleanIp, ct);
    }

    public async Task RecordFailedLoginStrikeAsync(string ip, string account, CancellationToken ct = default)
    {
        if (IsInternalOrLoopback(ip))
            return;

        string cleanIp = ip.Trim();
        string key = StrikeKey(cleanIp);
        int strikes = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StrikeWindow;
            return 0;
        });

        strikes++;
        cache.Set(key, strikes, StrikeWindow);

        if (strikes >= MaxStrikesBeforeBan)
        {
            cache.Remove(key);
            await BlockIpAsync(
                cleanIp,
                $"Excessive failed login attempts ({strikes} strikes)",
                LoginBanDuration,
                "AutoShield",
                ct);
        }
    }

    public async Task RecordHoneypotTriggerAsync(string ip, string form, CancellationToken ct = default)
    {
        if (IsInternalOrLoopback(ip))
            return;

        string cleanIp = ip.Trim();
        logger.LogWarning("Spam bot triggered honeypot field in form {Form} from {Ip}", form, cleanIp);

        await audit.WriteAsync("AutoShield", "security.honeypot.tripped", form, "Honeypot form field filled by bot", cleanIp, ct);

        await BlockIpAsync(
            cleanIp,
            $"Spam bot detected by honeypot in {form}",
            HoneypotBanDuration,
            "AutoShield",
            ct);
    }

    public async Task RecordSensitiveProbeAsync(string ip, string path, CancellationToken ct = default)
    {
        if (IsInternalOrLoopback(ip))
            return;

        string cleanIp = ip.Trim();
        logger.LogWarning("Exploit scanner probe detected for sensitive path {Path} from {Ip}", path, cleanIp);

        await audit.WriteAsync("AutoShield", "security.probe.detected", path, "Automated vulnerability scanner probe", cleanIp, ct);

        await BlockIpAsync(
            cleanIp,
            $"Automated vulnerability scan probe ({path})",
            ProbeBanDuration,
            "AutoShield",
            ct);
    }

    public async Task<List<BlockedIp>> ListBlockedIpsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        return await db.BlockedIps.AsNoTracking()
            .Where(b => b.ExpiresAt == null || b.ExpiresAt > now)
            .OrderByDescending(b => b.BlockedAt)
            .ToListAsync(ct);
    }
}

