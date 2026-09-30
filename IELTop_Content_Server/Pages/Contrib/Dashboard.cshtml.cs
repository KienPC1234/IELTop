using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Contrib;

[Authorize(Policy = "Contributor")]
public sealed class DashboardModel(
    IContributorService contributors,
    ISubmissionService submissions,
    IEditorService editors,
    IAuditService audit) : ContribPageModel(contributors)
{
    public List<Submission> Submissions { get; private set; } = new();
    public string DisplayName { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        DisplayName = Me!.DisplayName;
        Submissions = await submissions.ListForContributorAsync(ContributorId, ct);
        await SetEditorAsync(editors, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostWithdrawAsync(int id, CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        var (ok, error) = await submissions.WithdrawAsync(id, ContributorId, ct);
        await audit.WriteAsync(ContributorEmail,
            ok ? "submission.withdraw" : "submission.withdraw.failed",
            id.ToString(), error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok ? "Your submission was withdrawn." : error;
        return RedirectToPage();
    }
}
