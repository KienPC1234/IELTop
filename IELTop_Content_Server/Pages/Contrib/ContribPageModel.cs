using System.Security.Claims;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Contrib;

/// <summary>
/// Base for the contributor pages. Resolves the signed in contributor
/// from the cookie, so a page never trusts an id from the query string.
/// </summary>
public abstract class ContribPageModel : PageModel
{
    protected IContributorService Contributors { get; }

    protected ContribPageModel(IContributorService contributors)
    {
        Contributors = contributors;
    }

    public Contributor? Me { get; private set; }

    public int ContributorId { get; private set; }
    public string ContributorEmail { get; private set; } = string.Empty;

    /// <summary>True while this email is an approved editor.</summary>
    public bool IsEditor { get; private set; }

    protected async Task<bool> LoadAsync(CancellationToken ct)
    {
        ContributorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;
        if (ContributorId == 0)
            return false;

        Me = await Contributors.FindByIdAsync(ContributorId, ct);
        if (Me is null || Me.IsBlocked || !Me.IsActive)
            return false;

        ContributorEmail = Me.Email;
        ViewData["IsEditor"] = IsEditor;
        return true;
    }

    /// <summary>
    /// Marks whether the signed in contributor is an approved editor, so
    /// the shell can show the review link only when it leads somewhere.
    /// </summary>
    protected async Task SetEditorAsync(IEditorService editors, CancellationToken ct)
    {
        try
        {
            var approved = await editors.ListAsync(EditorApplicationStatus.Approved, ct);
            if (editors.IsEditor(ContributorEmail, approved))
            {
                IsEditor = true;
                ViewData["IsEditor"] = true;
            }
        }
        catch (Exception)
        {
            // A store blip hides the link; the page still works.
        }
    }

    protected string Ip => HttpContext.GetClientIp();
}
