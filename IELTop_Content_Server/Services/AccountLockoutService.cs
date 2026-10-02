using Microsoft.Extensions.Caching.Memory;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Tracks consecutive failed sign in attempts per account to prevent brute force
/// attacks and credential stuffing.
/// </summary>
public interface IAccountLockoutService
{
    Task<(bool IsLocked, TimeSpan Remaining)> CheckLockoutAsync(string accountKey);
    Task<(bool IsLocked, int FailedCount)> RecordFailedAttemptAsync(string accountKey, string ip, CancellationToken ct = default);
    Task ResetFailedAttemptsAsync(string accountKey);
    Task UnlockAccountAsync(string accountKey, string actor, CancellationToken ct = default);
}

public sealed class AccountLockoutService(
    IMemoryCache cache,
    IAuditService audit,
    ILogger<AccountLockoutService> logger) : IAccountLockoutService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(15);

    private static string FailKey(string key) => $"lockout:fail:{key.ToLowerInvariant().Trim()}";
    private static string LockKey(string key) => $"lockout:lock:{key.ToLowerInvariant().Trim()}";

    public Task<(bool IsLocked, TimeSpan Remaining)> CheckLockoutAsync(string accountKey)
    {
        if (string.IsNullOrWhiteSpace(accountKey))
            return Task.FromResult((false, TimeSpan.Zero));

        if (cache.TryGetValue(LockKey(accountKey), out DateTimeOffset lockedUntil))
        {
            var remaining = lockedUntil - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                return Task.FromResult((true, remaining));
            }
        }

        return Task.FromResult((false, TimeSpan.Zero));
    }

    public async Task<(bool IsLocked, int FailedCount)> RecordFailedAttemptAsync(
        string accountKey, string ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountKey))
            return (false, 0);

        string fKey = FailKey(accountKey);
        int current = cache.GetOrCreate(fKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = WindowDuration;
            return 0;
        });

        current++;
        cache.Set(fKey, current, WindowDuration);

        if (current >= MaxFailedAttempts)
        {
            var lockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
            cache.Set(LockKey(accountKey), lockedUntil, LockoutDuration);
            cache.Remove(fKey);

            logger.LogWarning("Account {Account} locked for {Minutes} minutes after {Count} failed attempts from {Ip}",
                accountKey, LockoutDuration.TotalMinutes, current, ip);

            await audit.WriteAsync("system", "security.account.locked", accountKey,
                $"Locked for {LockoutDuration.TotalMinutes} minutes after {current} failed attempts", ip, ct);

            return (true, current);
        }

        return (false, current);
    }

    public Task ResetFailedAttemptsAsync(string accountKey)
    {
        if (!string.IsNullOrWhiteSpace(accountKey))
        {
            cache.Remove(FailKey(accountKey));
            cache.Remove(LockKey(accountKey));
        }
        return Task.CompletedTask;
    }

    public async Task UnlockAccountAsync(string accountKey, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountKey))
            return;

        cache.Remove(FailKey(accountKey));
        cache.Remove(LockKey(accountKey));

        await audit.WriteAsync(actor, "security.account.unlocked", accountKey, "Account manually unlocked by admin", string.Empty, ct);
        logger.LogInformation("Account {Account} unlocked by {Actor}", accountKey, actor);
    }
}

