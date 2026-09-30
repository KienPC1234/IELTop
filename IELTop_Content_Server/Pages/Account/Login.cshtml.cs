using System.Security.Claims;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace IELTop_Content_Server.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class LoginModel(
    IAdminAuthService admins,
    IAuditService audit,
    ILogger<LoginModel> logger) : PageModel
{
    private const string Scheme = "admin";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string Username { get; private set; } = string.Empty;
    public string? Error { get; private set; }

    public void OnGet()
    {
        Username = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync(string username, string password, string? returnUrl, CancellationToken ct)
    {
        Username = username ?? string.Empty;
        ReturnUrl = returnUrl;

        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        bool ok = await admins.ValidateAsync(username ?? string.Empty, password ?? string.Empty, ct);
        if (!ok)
        {
            Error = "Wrong username or password.";
            await audit.WriteAsync(username ?? "unknown", "admin.login.failed", username ?? string.Empty,
                string.Empty, ip, ct);
            logger.LogWarning("Failed admin sign in for {User} from {Ip}", username, ip);
            return Page();
        }

        var user = await admins.FindAsync(username ?? string.Empty, ct);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user?.Id.ToString() ?? "0"),
            new(ClaimTypes.Name, username ?? string.Empty),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, Scheme);
        await HttpContext.SignInAsync(Scheme, new ClaimsPrincipal(identity));

        await audit.WriteAsync(username ?? string.Empty, "admin.login", username ?? string.Empty,
            string.Empty, ip, ct);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToPage("/Index");
    }
}
