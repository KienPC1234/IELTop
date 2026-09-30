using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Contrib;

[Authorize(Policy = "Contributor")]
public sealed class AccountModel(
    IContributorService contributors,
    ISubmissionService submissions,
    IEditorService editors,
    IAuditService audit) : ContribPageModel(contributors)
{
    public int SubmissionCount { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        SubmissionCount = await submissions.CountForContributorAsync(ContributorId, ct);
        await SetEditorAsync(editors, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(
        string current, string next, CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return RedirectToPage("/Contrib/SignIn");

        var (ok, error) = await Contributors.ChangePasswordAsync(
            ContributorId, current ?? string.Empty, next ?? string.Empty, ct);
        await audit.WriteAsync(ContributorEmail, "contributor.password", ContributorEmail,
            ok ? "ok" : error, Ip, ct);
        TempData[ok ? "Message" : "Error"] = ok ? "Your password was updated." : error;
        return RedirectToPage();
    }
}
