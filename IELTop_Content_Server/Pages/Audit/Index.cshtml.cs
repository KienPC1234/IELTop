using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Audit;

public sealed class IndexModel(IAuditService audit) : PageModel
{
    public List<AuditLog> Logs { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Logs = await audit.RecentAsync(300, ct);
    }
}
