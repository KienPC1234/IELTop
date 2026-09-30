using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Settings;

public sealed class NotificationsModel(
    IDbContextFactory<AppDbContext> dbFactory,
    INotificationService notify,
    IAuditService audit,
    ILlmReviewService review,
    IOptions<ContributeOptions> contribute,
    IOptions<SmtpOptions> smtp) : PageModel
{
    public bool Enabled { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; } = 587;
    public bool UseStartTls { get; private set; } = true;
    public string Username { get; private set; } = string.Empty;
    public string FromAddress { get; private set; } = string.Empty;
    public string FromName { get; private set; } = string.Empty;
    public string BaseUrl { get; private set; } = string.Empty;
    public bool ReviewEnabled => review.Enabled;
    public int AutoRejectBelowScore => contribute.Value.AutoRejectBelowScore;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var values = await LoadAsync(ct);
        Enabled = Flag(values, "smtp.enabled", smtp.Value.Enabled);
        Host = Text(values, "smtp.host", smtp.Value.Host);
        Port = Number(values, "smtp.port", smtp.Value.Port);
        UseStartTls = Flag(values, "smtp.tls", smtp.Value.UseStartTls);
        Username = Text(values, "smtp.username", smtp.Value.Username);
        FromAddress = Text(values, "smtp.from", smtp.Value.FromAddress);
        FromName = Text(values, "smtp.fromName", smtp.Value.FromName);
        BaseUrl = Text(values, "smtp.baseUrl", smtp.Value.BaseUrl);
    }

    public async Task<IActionResult> OnPostAsync(
        bool enabled, string host, int port, bool useStartTls, string username,
        string? password, string fromAddress, string fromName, string baseUrl, CancellationToken ct)
    {
        var values = new Dictionary<string, string>
        {
            ["smtp.enabled"] = enabled.ToString(),
            ["smtp.host"] = (host ?? string.Empty).Trim(),
            ["smtp.port"] = Math.Clamp(port, 1, 65535).ToString(),
            ["smtp.tls"] = useStartTls.ToString(),
            ["smtp.username"] = (username ?? string.Empty).Trim(),
            ["smtp.from"] = (fromAddress ?? string.Empty).Trim(),
            ["smtp.fromName"] = (fromName ?? string.Empty).Trim(),
            ["smtp.baseUrl"] = (baseUrl ?? string.Empty).Trim()
        };

        // Only overwrite the password when one was typed, so an admin can
        // edit other fields without re entering it.
        if (!string.IsNullOrWhiteSpace(password))
            values["smtp.password"] = password;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row is null)
                db.Settings.Add(new Setting { Key = key, Value = value });
            else
                row.Value = value;
        }
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(Actor, "settings.smtp", "smtp",
            $"enabled={enabled} host={values["smtp.host"]}", Ip, ct);
        TempData["Message"] = "Email settings were saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(string to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            TempData["Error"] = "Enter an address to send the test to.";
            return RedirectToPage();
        }

        await notify.QueueAsync(
            to,
            "IELTop content server test email",
            "This is a test message from the IELTop content server. "
            + "If you can read this, notifications are set up correctly.",
            ct);
        await audit.WriteAsync(Actor, "settings.smtp.test", to, string.Empty, Ip, ct);
        TempData["Message"] = $"A test email to {to} was queued. Check the outbox in a minute.";
        return RedirectToPage();
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Exclude secrets so a password value never loads into the page.
        var rows = await db.Settings
            .Where(s => s.Key.StartsWith("smtp.") && s.Key != "smtp.password")
            .AsNoTracking()
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string Text(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static bool Flag(Dictionary<string, string> map, string key, bool fallback) =>
        map.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int Number(Dictionary<string, string> map, string key, int fallback) =>
        map.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
