using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Signs admin users in and out of the portal, and makes sure there is
/// always one admin to sign in as on a fresh install.
/// </summary>
public interface IAdminAuthService
{
    Task<bool> ValidateAsync(string username, string password, CancellationToken ct = default);
    Task<AdminUser?> FindAsync(string username, CancellationToken ct = default);
    Task EnsureSeedAsync(CancellationToken ct = default);
    Task<(bool Ok, string Error)> ChangePasswordAsync(int userId, string current, string next, CancellationToken ct = default);
    Task<List<AdminUser>> ListAsync(CancellationToken ct = default);
    Task<(bool Ok, string Error)> CreateAsync(string username, string password, string role, CancellationToken ct = default);
    Task<bool> DeleteAsync(int userId, CancellationToken ct = default);
}

public sealed class AdminAuthService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<ServerOptions> serverOptions,
    ILogger<AdminAuthService> logger) : IAdminAuthService
{
    private readonly ServerOptions _server = serverOptions.Value;

    public async Task<AdminUser?> FindAsync(string username, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(a => a.Username == username, ct);
    }

    public async Task<bool> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.AdminUsers.FirstOrDefaultAsync(a => a.Username == username && a.IsActive, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
            return false;

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task EnsureSeedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AdminUsers.AnyAsync(ct))
            return;

        string username = string.IsNullOrWhiteSpace(_server.AdminUsername) ? "admin" : _server.AdminUsername.Trim();
        string password = _server.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = Guid.NewGuid().ToString("N")[..16];
            logger.LogWarning(
                "No admin password configured. A temporary password was generated: {Password}", password);
        }

        var (hash, salt) = PasswordHasher.Create(password);
        db.AdminUsers.Add(new AdminUser
        {
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = "Admin"
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded admin user {Username}", username);
    }

    public async Task<(bool Ok, string Error)> ChangePasswordAsync(
        int userId, string current, string next, CancellationToken ct = default)
    {
        if (next.Length < 8)
            return (false, "The new password must be at least 8 characters.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.AdminUsers.FirstOrDefaultAsync(a => a.Id == userId, ct);
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

    public async Task<List<AdminUser>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AdminUsers.OrderBy(a => a.Username).AsNoTracking().ToListAsync(ct);
    }

    public async Task<(bool Ok, string Error)> CreateAsync(
        string username, string password, string role, CancellationToken ct = default)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length < 3)
            return (false, "The username must be at least 3 characters.");
        if (password.Length < 8)
            return (false, "The password must be at least 8 characters.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AdminUsers.AnyAsync(a => a.Username == username, ct))
            return (false, "That username is taken.");

        var (hash, salt) = PasswordHasher.Create(password);
        db.AdminUsers.Add(new AdminUser
        {
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = string.IsNullOrWhiteSpace(role) ? "Admin" : role.Trim()
        });
        await db.SaveChangesAsync(ct);
        return (true, string.Empty);
    }

    public async Task<bool> DeleteAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AdminUsers.CountAsync(ct) <= 1)
            return false;
        var user = await db.AdminUsers.FirstOrDefaultAsync(a => a.Id == userId, ct);
        if (user is null)
            return false;
        db.AdminUsers.Remove(user);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
