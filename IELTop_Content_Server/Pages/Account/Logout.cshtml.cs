using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Account;

[Authorize(Policy = "Admin")]
public sealed class LogoutModel(IAuditService audit) : PageModel
{
    private const string Scheme = "admin";

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        string user = User.Identity?.Name ?? "unknown";
        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await HttpContext.SignOutAsync(Scheme);
        await audit.WriteAsync(user, "admin.logout", user, string.Empty, ip, ct);
        return RedirectToPage("/Account/Login");
    }
}
