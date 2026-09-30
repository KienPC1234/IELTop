using System.Text.Json;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Contrib;

[Authorize(Policy = "Contributor")]
public sealed class SubmissionModel(
    IContributorService contributors,
    ISubmissionService submissions,
    IAuditService audit) : ContribPageModel(contributors)
{
    public Submission Submission { get; private set; } = new();
    public List<SubmissionFileView> Files { get; private set; } = new();
    public List<string> Tags { get; private set; } = new();
    public List<string> Problems { get; private set; } = new();
    public List<string> Suggestions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        var row = await submissions.GetAsync(id, ct);
        if (row is null || row.ContributorId != ContributorId)
            return RedirectToPage("/Contrib/Dashboard");

        Submission = row;
        Files = await submissions.FilesAsync(id, ct);
        Tags = PaperService.Deserialize(row.TagsJson);
        ReadReview(row.ReviewJson);
        return Page();
    }

    public async Task<IActionResult> OnPostWithdrawAsync(int id, CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        var row = await submissions.GetAsync(id, ct);
        if (row is null || row.ContributorId != ContributorId)
            return RedirectToPage("/Contrib/Dashboard");

        var (ok, error) = await submissions.WithdrawAsync(id, ContributorId, ct);
        await audit.WriteAsync(ContributorEmail, "submission.withdraw", id.ToString(), error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok ? "Your submission was withdrawn." : error;
        return RedirectToPage("/Contrib/Dashboard");
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
}
