using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Submissions;

/// <summary>
/// Streams one attached file to an admin. The path is rebuilt from the
/// stored reference and stored name and checked to stay under the
/// submissions root, so no query string can reach another file.
/// </summary>
public sealed class DownloadModel(
    ISubmissionService submissions,
    IOptions<StorageOptions> storage,
    IAuditService audit) : PageModel
{
    private readonly StorageOptions _storage = storage.Value;

    public async Task<IActionResult> OnGetAsync(long fileId, CancellationToken ct)
    {
        var file = await submissions.FileAsync(fileId, ct);
        if (file is null || file.StoredName.Length == 0)
            return NotFound();

        var submission = await submissions.GetAsync(file.SubmissionId, ct);
        if (submission is null)
            return NotFound();

        string root = Path.GetFullPath(SubmissionPaths.Root(_storage));
        string safeRef = new string(submission.Reference.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        string safeName = SubmissionService.SafeFileName(file.StoredName);
        string full = Path.GetFullPath(Path.Combine(root, safeRef, safeName));
        if (!SubmissionService.IsInside(root, full) || !System.IO.File.Exists(full))
            return NotFound();

        await audit.WriteAsync(User.Identity?.Name ?? "unknown", "submission.download",
            file.FileName, file.SubmissionId.ToString(), Ip, ct);

        return PhysicalFile(full, "application/octet-stream", file.FileName);
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
