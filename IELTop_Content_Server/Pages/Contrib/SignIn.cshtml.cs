using System.Security.Claims;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Contrib;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class SignInModel(
    IContributorService contributors,
    ICaptchaService captcha,
    IAuditService audit,
    IIpAbuseGuard abuseGuard,
    ILogger<SignInModel> logger) : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public string? Error { get; private set; }

    public void OnGet()
    {
        ViewData["CaptchaEnabled"] = captcha.Enabled;
        ViewData["CaptchaSiteKey"] = captcha.SiteKey;
    }

    public async Task<IActionResult> OnPostAsync(
        string email,
        string password,
        [FromForm(Name = "cf-turnstile-response")] string? cfTurnstileResponse,
        CancellationToken ct)
    {
        Email = email ?? string.Empty;
        ViewData["CaptchaEnabled"] = captcha.Enabled;
        ViewData["CaptchaSiteKey"] = captcha.SiteKey;

        if (!string.IsNullOrEmpty(Request.Form["hp_website"]))
        {
            await abuseGuard.RecordHoneypotTriggerAsync(Ip, "Contributor Sign In", ct);
            Error = "Suspicious activity detected. Please try again.";
            return Page();
        }

        if (!await captcha.VerifyAsync(cfTurnstileResponse, Ip, ct))
        {
            Error = "The anti bot check failed. Try again.";
            return Page();
        }

        var (ok, error, user) = await contributors.SignInAsync(
            email ?? string.Empty, password ?? string.Empty, Ip, ct);
        if (!ok || user is null)
        {
            Error = error;
            await audit.WriteAsync(email ?? "unknown", "contributor.login.failed",
                email ?? string.Empty, error, Ip, ct);
            logger.LogWarning("Failed contributor sign in for {Email} from {Ip}: {Reason}", email, Ip, error);
            return Page();
        }

        await SignInAsync(user.Id, user.Email, ct);
        await audit.WriteAsync(user.Email, "contributor.login", user.Email, string.Empty, Ip, ct);
        return RedirectToPage("/Contrib/Dashboard");
    }

    private async Task SignInAsync(int id, string email, CancellationToken ct)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Role, "Contributor")
        };
        var identity = new ClaimsIdentity(claims, "contrib");
        await HttpContext.SignInAsync("contrib", new ClaimsPrincipal(identity));
    }

    private string Ip => HttpContext.GetClientIp();
}
