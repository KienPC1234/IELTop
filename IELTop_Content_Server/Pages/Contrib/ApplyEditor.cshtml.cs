using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Contrib;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class ApplyEditorModel(
    IEditorService editors,
    ICaptchaService captcha,
    INotificationService notify,
    IAuditService audit,
    IOptions<CaptchaOptions> captchaOptions,
    ILogger<ApplyEditorModel> logger) : PageModel
{    public string Email { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string Languages { get; private set; } = string.Empty;
    public string PortfolioUrl { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string? Error { get; private set; }
    public bool Done { get; private set; }

    public void OnGet()
    {
        ViewData["CaptchaEnabled"] = captchaOptions.Value.Enabled;
        ViewData["CaptchaSiteKey"] = captchaOptions.Value.SiteKey;
    }

    public async Task<IActionResult> OnPostAsync(
        string email, string fullName, string languages, string portfolioUrl, string reason,
        string? cfTurnstileResponse, CancellationToken ct)
    {
        Email = email ?? string.Empty;
        FullName = fullName ?? string.Empty;
        Languages = languages ?? string.Empty;
        PortfolioUrl = portfolioUrl ?? string.Empty;
        Reason = reason ?? string.Empty;
        ViewData["CaptchaEnabled"] = captchaOptions.Value.Enabled;
        ViewData["CaptchaSiteKey"] = captchaOptions.Value.SiteKey;

        if (!await captcha.VerifyAsync(cfTurnstileResponse, Ip, ct))
        {
            Error = "The anti bot check failed. Try again.";
            return Page();
        }

        var (ok, error, app) = await editors.ApplyAsync(
            email ?? string.Empty, fullName ?? string.Empty, reason ?? string.Empty,
            portfolioUrl ?? string.Empty, languages ?? string.Empty, ct);
        if (!ok || app is null)
        {
            Error = error;
            return Page();
        }

        await notify.QueueAsync(
            app.Email,
            "We received your IELTop editor application",
            $"Hello {app.FullName},\n\n"
            + "Thank you for applying to help review content. An admin will look at your "
            + "application and email you the decision.\n",
            ct);
        await audit.WriteAsync(app.Email, "editor.apply", app.Email, string.Empty, Ip, ct);
        logger.LogInformation("Editor application from {Email}", app.Email);

        Done = true;
        return Page();
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
