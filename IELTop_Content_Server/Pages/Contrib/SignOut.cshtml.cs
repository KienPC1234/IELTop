using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Contrib;

[Authorize(Policy = "Contributor")]
public sealed class SignOutModel(IAuditService audit) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Contrib/SignIn");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        string user = User.Identity?.Name ?? "unknown";
        await HttpContext.SignOutAsync("contrib");
        await audit.WriteAsync(user, "contributor.logout", user, string.Empty, Ip, ct);
        return RedirectToPage("/Contrib/SignIn");
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
