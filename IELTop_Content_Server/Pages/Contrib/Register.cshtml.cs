using System.Security.Claims;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Contrib;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class RegisterModel(
    IContributorService contributors,
    ICaptchaService captcha,
    INotificationService notify,
    IOptions<CaptchaOptions> captchaOptions,
    ILogger<RegisterModel> logger) : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Error { get; private set; }

    public void OnGet()
    {
        ViewData["CaptchaEnabled"] = captchaOptions.Value.Enabled;
        ViewData["CaptchaSiteKey"] = captchaOptions.Value.SiteKey;
    }

    public async Task<IActionResult> OnPostAsync(
        string email, string displayName, string password, string? cfTurnstileResponse, CancellationToken ct)
    {
        Email = email ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        ViewData["CaptchaEnabled"] = captchaOptions.Value.Enabled;
        ViewData["CaptchaSiteKey"] = captchaOptions.Value.SiteKey;

        if (!await captcha.VerifyAsync(cfTurnstileResponse, Ip, ct))
        {
            Error = "The anti bot check failed. Try again.";
            return Page();
        }

        var (ok, error, user) = await contributors.RegisterAsync(
            email ?? string.Empty, displayName ?? string.Empty, password ?? string.Empty, ct);
        if (!ok || user is null)
        {
            Error = error;
            return Page();
        }

        await notify.QueueAsync(
            user.Email,
            "Welcome to IELTop",
            $"Hello {user.DisplayName},\n\n"
            + "Your contributor account is ready. Sign in to submit practice papers, "
            + "listening audio, and texts. Every submission is reviewed and you get an "
            + "email when it is accepted or needs changes.\n\n"
            + "Thank you for helping build an open IELTS library.",
            ct);

        await SignInAsync(user.Id, user.Email, ct);
        logger.LogInformation("Contributor {Email} registered", user.Email);
        TempData["Message"] = "Your account is ready. Submit your first paper when you like.";
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

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
