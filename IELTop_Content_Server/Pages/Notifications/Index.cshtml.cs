using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Notifications;

public sealed class IndexModel(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<SmtpOptions> smtp) : PageModel
{
    public List<Notification> Rows { get; private set; } = new();
    public int Pending { get; private set; }
    public int Failed { get; private set; }
    public bool SmtpEnabled => smtp.Value.Enabled;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Pending = await db.Notifications.CountAsync(n => n.Status == NotificationStatus.Pending, ct);
        Failed = await db.Notifications.CountAsync(n => n.Status == NotificationStatus.Failed, ct);
        Rows = await db.Notifications
            .OrderByDescending(n => n.Id)
            .Take(300)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
