using System.Text;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace IELTop_Content_Server.Pages.Papers;

[EnableRateLimiting("form")]
public sealed class IndexModel(
    IPaperService papers,
    IAuditService audit) : PageModel
{
    public List<ExamPaper> Papers { get; private set; } = new();
    public string? Query { get; private set; }

    public async Task OnGetAsync(string? q, CancellationToken ct)
    {
        Query = q;
        Papers = await papers.AllForAdminAsync(q, ct);
    }

    public List<string> SkillsOf(ExamPaper row) => PaperService.Deserialize(row.SkillsJson);

    public string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    public async Task<IActionResult> OnGetDownloadAsync(string id, CancellationToken ct)
    {
        var row = await papers.GetAsync(id, ct);
        if (row is null)
            return NotFound();

        byte[] bytes = Encoding.UTF8.GetBytes(row.Json);
        return File(bytes, "application/json", $"{row.Id}.json");
    }

    public async Task<IActionResult> OnPostToggleAsync(string id, CancellationToken ct)
    {
        var row = await papers.GetAsync(id, ct);
        if (row is null)
        {
            TempData["Error"] = "That paper no longer exists.";
            return RedirectToPage();
        }

        await papers.SetPublishedAsync(id, !row.IsPublished, ct);
        await audit.WriteAsync(Actor, "paper.publish", id,
            (!row.IsPublished).ToString(), Ip, ct);
        TempData["Message"] = !row.IsPublished
            ? $"Paper {id} is now published."
            : $"Paper {id} is now a draft.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken ct)
    {
        bool removed = await papers.DeleteAsync(id, ct);
        await audit.WriteAsync(Actor, "paper.delete", id, removed ? "ok" : "missing", Ip, ct);
        TempData[removed ? "Message" : "Error"] = removed
            ? $"Paper {id} was deleted."
            : "That paper no longer exists.";
        return RedirectToPage();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
