using System.Text.Json;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace IELTop_Content_Server.Pages.Submissions;

[Authorize(Policy = "Editor")]
[EnableRateLimiting("form")]
public sealed class ReviewModel(
    ISubmissionService submissions,
    ILlmReviewService llm,
    IAuditService audit) : PageModel
{
    public Submission Submission { get; private set; } = new();
    public List<SubmissionFileView> Files { get; private set; } = new();
    public List<string> Problems { get; private set; } = new();
    public List<string> Suggestions { get; private set; } = new();
    public string SuggestedTags { get; private set; } = string.Empty;
    public bool LlmEnabled => llm.Enabled;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var row = await submissions.GetAsync(id, ct);
        if (row is null)
            return RedirectToPage("/Submissions/Index");

        Submission = row;
        Files = await submissions.FilesAsync(id, ct);
        SuggestedTags = string.Join(", ", PaperService.Deserialize(row.TagsJson));
        ReadReview(row.ReviewJson);
        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(
        int id, string? extraTags, string? decisionNote, CancellationToken ct)
    {
        var (ok, error) = await submissions.AcceptAsync(
            id, decisionNote ?? string.Empty, extraTags ?? string.Empty, Actor, AdminId, ct);
        await audit.WriteAsync(Actor, ok ? "submission.accept" : "submission.accept.failed",
            id.ToString(), error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok
            ? "The submission was accepted and published to the catalog."
            : error;
        return RedirectToPage("/Submissions/Index");
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string reason, CancellationToken ct)
    {
        var (ok, error) = await submissions.RejectAsync(id, reason ?? string.Empty, Actor, AdminId, ct);
        await audit.WriteAsync(Actor, ok ? "submission.reject" : "submission.reject.failed",
            id.ToString(), error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok
            ? "The submission was rejected and the author was notified."
            : error;
        return RedirectToPage("/Submissions/Index");
    }

    public async Task<IActionResult> OnPostReRunAsync(int id, CancellationToken ct)
    {
        var row = await submissions.GetAsync(id, ct);
        if (row is null)
            return RedirectToPage("/Submissions/Index");

        if (!llm.Enabled)
        {
            TempData["Warning"] = "No AI model is configured. Go to Settings to set up the model.";
            return RedirectToPage(new { id });
        }

        var files = await submissions.FilesAsync(id, ct);
        string paperText = string.Join("\n\n", files.Where(f => f.Text.Length > 0).Select(f => f.Text));

        var result = await llm.ReviewAsync(row.Title, row.Source, row.License, paperText, ct);

        if (!result.Ran)
        {
            TempData["Error"] = $"AI review failed: {result.Error}";
            return RedirectToPage(new { id });
        }

        await submissions.SaveReviewAsync(id, result, ct);
        await audit.WriteAsync(Actor, "submission.rerun-review", id.ToString(),
            $"score={result.Score} passed={result.Passed}", Ip, ct);
        TempData["Message"] = $"AI review done. Score: {result.Score}/100, passed: {result.Passed}.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSavePaperAsync(int id, long fileId, string paperContent, CancellationToken ct)
    {
        var (ok, error) = await submissions.UpdatePaperFileAsync(id, fileId, paperContent ?? string.Empty, ct);
        if (!ok)
        {
            TempData["Error"] = $"Could not save paper: {error}";
        }
        else
        {
            TempData["Message"] = "Paper content saved successfully.";
            await audit.WriteAsync(Actor, "submission.edit-paper", id.ToString(), $"fileId={fileId}", Ip, ct);
        }
        return RedirectToPage(new { id });
    }

    private void ReadReview(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Problems = ReadList(doc.RootElement, "problems");
            Suggestions = ReadList(doc.RootElement, "suggestions");
        }
        catch (JsonException)
        {
            // A malformed stored review is simply not shown.
        }
    }

    private static List<string> ReadList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
            return new();
        return element.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private int? AdminId => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id)
        ? id
        : null;
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
