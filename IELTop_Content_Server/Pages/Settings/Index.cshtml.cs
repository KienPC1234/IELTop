using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Settings;

public sealed class IndexModel(
    ISettingsService settings,
    IAdminAuthService admins,
    IPaperService papers,
    IAuditService audit) : PageModel
{
    public ServerSettings Server { get; private set; } = new();
    public List<AdminUser> Admins { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Server = await settings.GetServerSettingsAsync(ct);
        Admins = await admins.ListAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(
        string name, string? skills, bool anonymous, bool code, bool login, CancellationToken ct)
    {
        var values = new Dictionary<string, string>
        {
            ["server.name"] = (name ?? string.Empty).Trim(),
            ["auth.anonymous"] = anonymous.ToString(),
            ["auth.code"] = code.ToString(),
            ["auth.login"] = login.ToString(),
            ["server.skills"] = System.Text.Json.JsonSerializer.Serialize(
                (skills ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToLowerInvariant())
                    .Distinct()
                    .ToList())
        };

        await settings.SetManyAsync(values, ct);
        await audit.WriteAsync(Actor, "settings.save", "server",
            $"anon={anonymous} code={code} login={login}", Ip, ct);
        TempData["Message"] = "Settings were saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(
        string current, string next, CancellationToken ct)
    {
        var me = await admins.FindAsync(User.Identity?.Name ?? string.Empty, ct);
        if (me is null)
        {
            TempData["Error"] = "Your admin account could not be found.";
            return RedirectToPage();
        }

        var (ok, error) = await admins.ChangePasswordAsync(me.Id, current ?? string.Empty, next ?? string.Empty, ct);
        await audit.WriteAsync(Actor, "admin.password", me.Username, ok ? "ok" : error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok ? "Your password was updated." : error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddAdminAsync(string username, string password, CancellationToken ct)
    {
        var (ok, error) = await admins.CreateAsync(username ?? string.Empty, password ?? string.Empty, "Admin", ct);
        await audit.WriteAsync(Actor, "admin.create", username ?? string.Empty, ok ? "ok" : error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok ? $"Admin {username} was created." : error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAdminAsync(int id, CancellationToken ct)
    {
        bool removed = await admins.DeleteAsync(id, ct);
        await audit.WriteAsync(Actor, "admin.delete", id.ToString(), removed ? "ok" : "refused", Ip, ct);
        TempData[removed ? "Message" : "Error"] = removed
            ? "The admin account was deleted."
            : "At least one admin account must remain.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRebuildAsync(CancellationToken ct)
    {
        await papers.RebuildSummariesAsync(ct);
        await audit.WriteAsync(Actor, "papers.rebuild", "all", "ok", Ip, ct);
        TempData["Message"] = "Paper summaries were rebuilt.";
        return RedirectToPage();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
