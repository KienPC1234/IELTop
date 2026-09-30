using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IELTop_Content_Server.Pages.Contributors;

public sealed class IndexModel(
    IContributorService contributors,
    IDbContextFactory<AppDbContext> dbFactory,
    IAuditService audit) : PageModel
{
    public List<Contributor> Rows { get; private set; } = new();
    public string? Query { get; private set; }
    private Dictionary<int, int> Counts { get; set; } = new();

    public async Task OnGetAsync(string? q, CancellationToken ct)
    {
        Query = q;
        var all = await contributors.ListAsync(ct);
        Rows = string.IsNullOrWhiteSpace(q)
            ? all
            : all.Where(c => c.Email.Contains(q, StringComparison.OrdinalIgnoreCase)
                || c.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grouped = await db.Submissions
            .GroupBy(s => s.ContributorId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToListAsync(ct);
        Counts = grouped.ToDictionary(g => g.Id, g => g.Count);
    }

    public int CountFor(int id) => Counts.GetValueOrDefault(id);

    public async Task<IActionResult> OnPostToggleActiveAsync(int id, CancellationToken ct)
    {
        var user = await contributors.FindByIdAsync(id, ct);
        if (user is null)
        {
            TempData["Error"] = "That account no longer exists.";
            return RedirectToPage();
        }

        await contributors.SetActiveAsync(id, !user.IsActive, ct);
        await audit.WriteAsync(Actor, "contributor.active", user.Email, !user.IsActive ? "active" : "inactive", Ip, ct);
        TempData["Message"] = $"{user.Email} is now {(!user.IsActive ? "active" : "inactive")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleBlockAsync(int id, CancellationToken ct)
    {
        var user = await contributors.FindByIdAsync(id, ct);
        if (user is null)
        {
            TempData["Error"] = "That account no longer exists.";
            return RedirectToPage();
        }

        await contributors.SetBlockedAsync(id, !user.IsBlocked, ct);
        await audit.WriteAsync(Actor, "contributor.block", user.Email, !user.IsBlocked ? "blocked" : "unblocked", Ip, ct);
        TempData["Message"] = $"{user.Email} is now {(!user.IsBlocked ? "blocked" : "unblocked")}.";
        return RedirectToPage();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
