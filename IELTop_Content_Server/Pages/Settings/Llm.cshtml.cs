using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Settings;

[Authorize(Policy = "Admin")]
[EnableRateLimiting("form")]
public sealed class LlmModel(
    ILlmReviewService review,
    IOptions<LlmOptions> options,
    IAuditService audit) : PageModel
{
    private readonly LlmOptions _llm = options.Value;

    public string BaseUrl { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public bool HasApiKey { get; private set; }
    public double Temperature { get; private set; }
    public int MaxTokens { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public bool ReviewEnabled { get; private set; }

    // Test result shown after POST TestConnection
    public bool? TestOk { get; private set; }
    public string TestMessage { get; private set; } = string.Empty;
    public string TestRaw { get; private set; } = string.Empty;

    public void OnGet() => Load();

    /// <summary>
    /// Test kết nối đến model hiện tại bằng một prompt nhỏ.
    /// Chỉ admin mới gọi được, rate-limited bởi "form" policy.
    /// Không thay đổi config, chỉ đọc và ping.
    /// </summary>
    public async Task<IActionResult> OnPostTestAsync(CancellationToken ct)
    {
        Load();
        if (!ReviewEnabled)
        {
            TestOk = false;
            TestMessage = "No model is configured. Set Base URL and Model name in appsettings first.";
            return Page();
        }

        var result = await review.ReviewAsync(
            title: "Test ping",
            source: "internal",
            license: "internal",
            paperText: "This is a short connectivity test. Reply with the required JSON.",
            ct: ct);

        TestOk = result.Ran;
        TestRaw = result.RawJson;
        TestMessage = result.Ran
            ? $"Connected. Score: {result.Score}, passed: {result.Passed}. Summary: {result.Summary}"
            : $"Failed: {result.Error}";

        await audit.WriteAsync(
            Actor, "llm.test", _llm.Model,
            result.Ran ? "ok" : result.Error, Ip, ct);

        return Page();
    }

    private void Load()
    {
        BaseUrl = _llm.BaseUrl;
        Model = _llm.Model;
        HasApiKey = !string.IsNullOrWhiteSpace(_llm.ApiKey);
        Temperature = _llm.Temperature;
        MaxTokens = _llm.MaxTokens;
        TimeoutSeconds = _llm.TimeoutSeconds;
        ReviewEnabled = _llm.Enabled;
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

