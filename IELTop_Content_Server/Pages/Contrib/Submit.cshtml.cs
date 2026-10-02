using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Contrib;

[Authorize(Policy = "Contributor")]
[EnableRateLimiting("form")]
public sealed class SubmitModel(
    IContributorService contributors,
    ISubmissionService submissions,
    ICaptchaService captcha,
    ILlmReviewService review,
    IStatsService stats,
    IAuditService audit,
    IIpAbuseGuard abuseGuard,
    IOptions<ContributeOptions> limits) : ContribPageModel(contributors)
{
    private readonly ContributeOptions _limits = limits.Value;

    public string Title { get; private set; } = string.Empty;
    public string AuthorName { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string License { get; private set; } = string.Empty;
    public string Note { get; private set; } = string.Empty;
    public string OnlineContent { get; private set; } = string.Empty;
    public string? Error { get; private set; }
    public int MaxFiles => Math.Max(1, _limits.MaxFilesPerSubmission);
    public int MaxMb => Math.Max(1, _limits.MaxSubmissionMb);
    public bool ReviewEnabled => review.Enabled;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        AuthorName = Me!.DisplayName;
        ViewData["CaptchaEnabled"] = captcha.Enabled;
        ViewData["CaptchaSiteKey"] = captcha.SiteKey;
        if (!captcha.Enabled)
            ViewData["MathChallenge"] = captcha.CreateMathChallenge();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        string title, string authorName, string source, string license, string note,
        bool authorConfirm,
        [FromForm(Name = "cf-turnstile-response")] string? cfTurnstileResponse,
        [FromForm(Name = "captcha_answer")] string? captchaAnswer,
        [FromForm(Name = "captcha_signature")] string? captchaSignature,
        string? onlineContent,
        List<IFormFile>? files, CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        Title = title ?? string.Empty;
        AuthorName = string.IsNullOrWhiteSpace(authorName) ? Me!.DisplayName : authorName;
        Source = source ?? string.Empty;
        License = license ?? string.Empty;
        Note = note ?? string.Empty;
        OnlineContent = onlineContent ?? string.Empty;
        ViewData["CaptchaEnabled"] = captcha.Enabled;
        ViewData["CaptchaSiteKey"] = captcha.SiteKey;

        if (!string.IsNullOrEmpty(Request.Form["hp_website"]))
        {
            await abuseGuard.RecordHoneypotTriggerAsync(Ip, "Paper Submission", ct);
            Error = "Suspicious activity detected. Please try again.";
            if (!captcha.Enabled)
                ViewData["MathChallenge"] = captcha.CreateMathChallenge();
            return Page();
        }

        if (!await captcha.VerifySubmissionAsync(cfTurnstileResponse, captchaAnswer, captchaSignature, Ip, ct))
        {
            Error = "The human verification check failed. Please complete the verification challenge.";
            if (!captcha.Enabled)
                ViewData["MathChallenge"] = captcha.CreateMathChallenge();
            return Page();
        }

        if (!authorConfirm)
        {
            Error = "Confirm you have the right to share this content.";
            return Page();
        }

        // A soft per account limit so one account cannot flood review.
        int recent = await submissions.CountRecentForContributorAsync(
            ContributorId, TimeSpan.FromHours(1), ct);
        if (recent >= Math.Max(1, _limits.SubmissionsPerHour))
        {
            Error = $"You have reached {_limits.SubmissionsPerHour} submissions this hour. Try again later.";
            return Page();
        }

        var uploads = new List<SubmissionUpload>();
        long totalBytes = 0;
        long cap = Math.Max(1, _limits.MaxSubmissionMb) * 1024L * 1024L;
        foreach (var file in files ?? new List<IFormFile>())
        {
            if (file.Length == 0)
                continue;
            if (uploads.Count >= MaxFiles)
            {
                Error = $"At most {MaxFiles} files per submission.";
                return Page();
            }
            if (file.Length > cap)
            {
                Error = $"{file.FileName} is larger than {MaxMb} MB.";
                return Page();
            }

            totalBytes += file.Length;
            if (totalBytes > cap)
            {
                Error = $"The whole submission is larger than {MaxMb} MB.";
                return Page();
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            uploads.Add(new SubmissionUpload(
                Path.GetFileName(file.FileName),
                file.ContentType ?? "application/octet-stream",
                memory.ToArray()));
        }

        if (!string.IsNullOrWhiteSpace(onlineContent))
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(onlineContent.Trim());
            string ext = onlineContent.TrimStart().StartsWith("{") ? ".json" : ".txt";
            string docName = ext == ".json" ? "paper.json" : "paper.txt";
            string contentType = ext == ".json" ? "application/json" : "text/plain";
            uploads.Insert(0, new SubmissionUpload(docName, contentType, bytes));
        }

        if (uploads.Count == 0)
        {
            Error = "Provide your paper content either by writing in the editor or by attaching files.";
            if (!captcha.Enabled)
                ViewData["MathChallenge"] = captcha.CreateMathChallenge();
            return Page();
        }

        var request = new SubmissionRequest(
            ContributorId, AuthorName, Title, Note, Source, License, uploads);

        var (ok, error, created) = await submissions.CreateAsync(request, reviewOverride: null, ct);
        if (!ok || created is null)
        {
            Error = error;
            return Page();
        }

        stats.RecordSubmission();
        await audit.WriteAsync(ContributorEmail, "submission.create", created.Reference,
            $"{uploads.Count} file(s), status {created.Status}", Ip, ct);

        TempData["Message"] = created.Status switch
        {
            Models.SubmissionStatus.Rejected =>
                $"Submission {created.Reference} did not pass the automated review. See the reason on your dashboard.",
            Models.SubmissionStatus.Draft =>
                $"Submission {created.Reference} was saved. The automated review is off, so an editor will look at it.",
            _ => $"Submission {created.Reference} was sent for review."
        };
        return RedirectToPage("/Contrib/Submission", new { id = created.Id });
    }
}
