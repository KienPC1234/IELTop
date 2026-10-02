using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages;

public sealed class IndexModel : PageModel
{
    private readonly IProtocolService _protocol;
    private readonly IAudioService _audio;
    private readonly IAuditService _audit;
    private readonly ISubmissionService _submissions;
    private readonly IEditorService _editors;
    private readonly IStatsService _stats;
    private readonly INotificationService _notifications;
    private readonly IContentCache _cache;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IOptions<DatabaseOptions> _database;
    private readonly IOptions<CacheOptions> _cacheOptions;
    private readonly IOptions<CaptchaOptions> _captcha;
    private readonly IOptions<SmtpOptions> _smtp;

    public IndexModel(
        IProtocolService protocol,
        IAudioService audio,
        IAuditService audit,
        ISubmissionService submissions,
        IEditorService editors,
        IStatsService stats,
        INotificationService notifications,
        IContentCache cache,
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<DatabaseOptions> database,
        IOptions<CacheOptions> cacheOptions,
        IOptions<CaptchaOptions> captcha,
        IOptions<SmtpOptions> smtp)
    {
        _protocol = protocol;
        _audio = audio;
        _audit = audit;
        _submissions = submissions;
        _editors = editors;
        _stats = stats;
        _notifications = notifications;
        _cache = cache;
        _dbFactory = dbFactory;
        _database = database;
        _cacheOptions = cacheOptions;
        _captcha = captcha;
        _smtp = smtp;
    }

    public long PaperCount { get; private set; }
    public int AudioCount { get; private set; }
    public int InReview { get; private set; }
    public int PendingEditors { get; private set; }
    public int ContributorCount { get; private set; }
    public long TodayApiCalls { get; private set; }
    public long TodayDownloads { get; private set; }
    public long TodayAudio { get; private set; }
    public int MailPending { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;
    public List<string> AuthModes { get; private set; } = new();
    public List<string> Skills { get; private set; } = new();
    public string DatabaseProvider { get; private set; } = string.Empty;
    public string CacheProvider { get; private set; } = string.Empty;
    public long ContentVersion { get; private set; }
    public string Uptime { get; private set; } = string.Empty;
    public string WorkingSet { get; private set; } = string.Empty;
    public List<AuditLog> Recent { get; private set; } = new();
    public bool LoginEnabled { get; private set; }
    public bool CaptchaEnabled => _captcha.Value.Enabled;
    public bool SmtpEnabled => _smtp.Value.Enabled;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true
            || !User.HasClaim(System.Security.Claims.ClaimTypes.Role, "Admin"))
        {
            return RedirectToPage("/About");
        }

        var greeting = await _protocol.GreetingAsync(ct);
        AuthModes = greeting.Auth;
        Skills = greeting.Skills;
        LoginEnabled = greeting.Auth.Contains("login", StringComparer.OrdinalIgnoreCase);

        PaperCount = await _protocol.CountPapersAsync(ct);
        AudioCount = (await _audio.ListAsync(ct)).Count;
        InReview = await _submissions.CountByStatusAsync(SubmissionStatus.InReview, ct);
        PendingEditors = await _editors.CountPendingAsync(ct);
        MailPending = await _notifications.PendingCountAsync(ct);

        var today = await _stats.TodayAsync(ct);
        TodayApiCalls = today.ApiCalls;
        TodayDownloads = today.Downloads;
        TodayAudio = today.AudioServed;

        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            ContributorCount = await db.Contributors.CountAsync(ct);
        }

        var request = HttpContext.Request;
        BaseUrl = $"{request.Scheme}://{request.Host}";
        DatabaseProvider = _database.Value.Provider;
        CacheProvider = _cacheOptions.Value.Provider;
        ContentVersion = await _cache.VersionAsync(ct);

        var started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var up = DateTime.UtcNow - started;
        Uptime = up.TotalDays >= 1
            ? $"{(int)up.TotalDays}d {up.Hours}h"
            : up.TotalHours >= 1
                ? $"{(int)up.TotalHours}h {up.Minutes}m"
                : $"{(int)up.TotalMinutes}m {up.Seconds}s";
        WorkingSet = $"{System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB";
        Recent = await _audit.RecentAsync(8, ct);
        return Page();
    }
}
