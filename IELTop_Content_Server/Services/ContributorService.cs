using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Contributor accounts for the public portal. An email and password,
/// kept apart from admin users so a contributor can never open the
/// admin portal.
/// </summary>
public interface IContributorService
{
    Task<Contributor?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<Contributor?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<(bool Ok, string Error, Contributor? User)> RegisterAsync(
        string email, string displayName, string password, CancellationToken ct = default);
    Task<(bool Ok, string Error, Contributor? User)> SignInAsync(
        string email, string password, CancellationToken ct = default);
    Task<(bool Ok, string Error, Contributor? User)> SignInAsync(
        string email, string password, string ip, CancellationToken ct = default);
    Task<(bool Ok, string Error)> ChangePasswordAsync(
        int contributorId, string current, string next, CancellationToken ct = default);
    Task<List<Contributor>> ListAsync(CancellationToken ct = default);
    Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default);
    Task<bool> SetBlockedAsync(int id, bool blocked, CancellationToken ct = default);
}

public sealed class ContributorService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAccountLockoutService lockout,
    IIpAbuseGuard abuseGuard,
    IEmailCheckService emailCheck) : IContributorService
{
    public async Task<Contributor?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        string key = Normalize(email);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Contributors.AsNoTracking().FirstOrDefaultAsync(c => c.Email == key, ct);
    }

    public async Task<Contributor?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Contributors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<(bool Ok, string Error, Contributor? User)> RegisterAsync(
        string email, string displayName, string password, CancellationToken ct = default)
    {
        string key = Normalize(email);
        var check = await emailCheck.ValidateAsync(key, ct);
        if (!check.Valid)
            return (false, check.Error, null);
        if (password.Length < 8)
            return (false, "The password must be at least 8 characters.", null);
        if (password.Length > 256)
            return (false, "The password is too long.", null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Contributors.AnyAsync(c => c.Email == key, ct))
            return (false, "An account with that email already exists.", null);

        string name = string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim();
        if (name.Length > 120)
            name = name[..120];

        var (hash, salt) = PasswordHasher.Create(password);
        var user = new Contributor
        {
            Email = key,
            DisplayName = name,
            PasswordHash = hash,
            PasswordSalt = salt
        };
        db.Contributors.Add(user);
        await db.SaveChangesAsync(ct);
        return (true, string.Empty, user);
    }

    public Task<(bool Ok, string Error, Contributor? User)> SignInAsync(
        string email, string password, CancellationToken ct = default) =>
        SignInAsync(email, password, "internal", ct);

    public async Task<(bool Ok, string Error, Contributor? User)> SignInAsync(
        string email, string password, string ip, CancellationToken ct = default)
    {
        string key = Normalize(email);
        string lockKey = $"contrib:{key}";
        var (isLocked, remaining) = await lockout.CheckLockoutAsync(lockKey);
        if (isLocked)
        {
            int mins = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return (false, $"Account is temporarily locked due to repeated failed logins. Please try again in {mins} minutes.", null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Contributors.FirstOrDefaultAsync(c => c.Email == key, ct);

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            var (nowLocked, count) = await lockout.RecordFailedAttemptAsync(lockKey, ip, ct);
            await abuseGuard.RecordFailedLoginStrikeAsync(ip, email, ct);

            if (nowLocked)
            {
                return (false, "Account has been locked for 15 minutes due to 5 failed sign in attempts.", null);
            }

            int left = Math.Max(0, 5 - count);
            string extra = left > 0 ? $" ({left} attempts remaining before temporary lockout)" : string.Empty;
            return (false, $"Wrong email or password.{extra}", null);
        }

        if (user.IsBlocked)
            return (false, "This account is blocked. Contact the server team.", null);
        if (!user.IsActive)
            return (false, "This account is not active yet.", null);

        await lockout.ResetFailedAttemptsAsync(lockKey);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, string.Empty, user);
    }

    public async Task<(bool Ok, string Error)> ChangePasswordAsync(
        int contributorId, string current, string next, CancellationToken ct = default)
    {
        if (next.Length < 8)
            return (false, "The new password must be at least 8 characters.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Contributors.FirstOrDefaultAsync(c => c.Id == contributorId, ct);
        if (user is null)
            return (false, "The account no longer exists.");
        if (!PasswordHasher.Verify(current, user.PasswordHash, user.PasswordSalt))
            return (false, "The current password is wrong.");

        var (hash, salt) = PasswordHasher.Create(next);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await db.SaveChangesAsync(ct);
        return (true, string.Empty);
    }

    public async Task<List<Contributor>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Contributors.OrderBy(c => c.Email).AsNoTracking().ToListAsync(ct);
    }

    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Contributors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (user is null)
            return false;
        user.IsActive = active;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetBlockedAsync(int id, bool blocked, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Contributors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (user is null)
            return false;
        user.IsBlocked = blocked;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static string Normalize(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool LooksLikeEmail(string email)
    {
        int at = email.IndexOf('@');
        int dot = email.LastIndexOf('.');
        return email.Length is >= 5 and <= 254
               && at > 0 && dot > at + 1 && dot < email.Length - 1
               && !email.Contains(' ') && !email.Contains('\n') && !email.Contains('\r');
    }
}
