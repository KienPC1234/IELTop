using System.Security.Cryptography;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Credentials;

public sealed class IndexModel(
    IDbContextFactory<AppDbContext> dbFactory,
    IAuditService audit,
    IContentCache cache,
    IOptions<ServerOptions> serverOptions) : PageModel
{
    public List<AccessCode> Codes { get; private set; } = new();
    public List<LoginAccount> Accounts { get; private set; } = new();
    public bool MasterCodeSet { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        MasterCodeSet = !string.IsNullOrWhiteSpace(serverOptions.Value.MasterCode);
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Codes = await db.AccessCodes.OrderBy(c => c.Name).AsNoTracking().ToListAsync(ct);
        Accounts = await db.LoginAccounts.OrderBy(a => a.Username).AsNoTracking().ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateCodeAsync(
        string name, string? code, string? expires, CancellationToken ct)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            TempData["Error"] = "Give the code a name.";
            return RedirectToPage();
        }

        string value = (code ?? string.Empty).Trim();
        if (value.Length == 0)
            value = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        if (value.Length < 4)
        {
            TempData["Error"] = "The code must be at least 4 characters.";
            return RedirectToPage();
        }

        DateTimeOffset? expiry = null;
        if (DateTime.TryParse(expires, out var parsed))
            expiry = new DateTimeOffset(parsed.Date, TimeSpan.Zero);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AccessCodes.AnyAsync(c => c.Code == value, ct))
        {
            TempData["Error"] = "That code already exists.";
            return RedirectToPage();
        }

        db.AccessCodes.Add(new AccessCode { Name = name, Code = value, ExpiresAt = expiry });
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
        await audit.WriteAsync(Actor, "code.create", name, value, Ip, ct);
        TempData["Message"] = $"Access code {value} was created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleCodeAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null)
        {
            TempData["Error"] = "That code no longer exists.";
            return RedirectToPage();
        }

        row.IsActive = !row.IsActive;
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
        await cache.RemoveAsync("code:" + row.Code, ct);
        await audit.WriteAsync(Actor, "code.toggle", row.Name, row.IsActive.ToString(), Ip, ct);
        TempData["Message"] = $"Code {row.Name} is now {(row.IsActive ? "active" : "disabled")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCodeAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null)
        {
            TempData["Error"] = "That code no longer exists.";
            return RedirectToPage();
        }

        db.AccessCodes.Remove(row);
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
        await cache.RemoveAsync("code:" + row.Code, ct);
        await audit.WriteAsync(Actor, "code.delete", row.Name, row.Code, Ip, ct);
        TempData["Message"] = $"Code {row.Name} was deleted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateAccountAsync(
        string username, string? displayName, string password, CancellationToken ct)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length < 3)
        {
            TempData["Error"] = "The username must be at least 3 characters.";
            return RedirectToPage();
        }
        if ((password ?? string.Empty).Length < 8)
        {
            TempData["Error"] = "The password must be at least 8 characters.";
            return RedirectToPage();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.LoginAccounts.AnyAsync(a => a.Username == username, ct))
        {
            TempData["Error"] = "That username is taken.";
            return RedirectToPage();
        }

        var (hash, salt) = PasswordHasher.Create(password ?? string.Empty);
        db.LoginAccounts.Add(new LoginAccount
        {
            Username = username,
            DisplayName = (displayName ?? string.Empty).Trim(),
            PasswordHash = hash,
            PasswordSalt = salt
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(Actor, "account.create", username, string.Empty, Ip, ct);
        TempData["Message"] = $"Login account {username} was created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id, string password, CancellationToken ct)
    {
        if ((password ?? string.Empty).Length < 8)
        {
            TempData["Error"] = "The password must be at least 8 characters.";
            return RedirectToPage();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.LoginAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            TempData["Error"] = "That account no longer exists.";
            return RedirectToPage();
        }

        var (hash, salt) = PasswordHasher.Create(password ?? string.Empty);
        row.PasswordHash = hash;
        row.PasswordSalt = salt;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("account:" + id, ct);
        await audit.WriteAsync(Actor, "account.reset", row.Username, string.Empty, Ip, ct);
        TempData["Message"] = $"Password for {row.Username} was reset.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAccountAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.LoginAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            TempData["Error"] = "That account no longer exists.";
            return RedirectToPage();
        }

        row.IsActive = !row.IsActive;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("account:" + id, ct);
        await audit.WriteAsync(Actor, "account.toggle", row.Username, row.IsActive.ToString(), Ip, ct);
        TempData["Message"] = $"{row.Username} is now {(row.IsActive ? "active" : "disabled")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAccountAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.LoginAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            TempData["Error"] = "That account no longer exists.";
            return RedirectToPage();
        }

        db.LoginAccounts.Remove(row);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("account:" + id, ct);
        await audit.WriteAsync(Actor, "account.delete", row.Username, string.Empty, Ip, ct);
        TempData["Message"] = $"Account {row.Username} was deleted.";
        return RedirectToPage();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
