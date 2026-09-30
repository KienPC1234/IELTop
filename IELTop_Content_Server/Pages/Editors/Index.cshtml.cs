using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Editors;

public sealed class IndexModel(
    IEditorService editors,
    IAuditService audit) : PageModel
{
    public List<EditorApplication> Rows { get; private set; } = new();
    public string? Status { get; private set; }
    public int Pending { get; private set; }

    public async Task OnGetAsync(string? status, CancellationToken ct)
    {
        Status = status;
        EditorApplicationStatus? filter =
            Enum.TryParse<EditorApplicationStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
        Rows = await editors.ListAsync(filter, ct);
        Pending = await editors.CountPendingAsync(ct);
    }

    public async Task<IActionResult> OnPostApproveAsync(
        int id, string? role, string? note, CancellationToken ct)
    {
        var (ok, error) = await editors.ApproveAsync(id, note ?? string.Empty, role ?? "Editor", AdminId, ct);
        await audit.WriteAsync(Actor, ok ? "editor.approve" : "editor.approve.failed",
            id.ToString(), error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok
            ? "The applicant was approved and an invite email was queued."
            : error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeclineAsync(int id, string? note, CancellationToken ct)
    {
        var (ok, error) = await editors.DeclineAsync(id, note ?? string.Empty, AdminId, ct);
        await audit.WriteAsync(Actor, ok ? "editor.decline" : "editor.decline.failed",
            id.ToString(), error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok
            ? "The application was declined and the applicant was notified."
            : error;
        return RedirectToPage();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private int? AdminId => int.TryParse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : null;
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
