using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Papers;

[EnableRateLimiting("form")]
public sealed class ImportModel(
    IPaperService papers,
    IAuditService audit,
    IOptions<StorageOptions> storage) : PageModel
{
    public List<string> Results { get; private set; } = new();
    public int Saved { get; private set; }
    public int Skipped { get; private set; }
    public string SuggestedFolder { get; private set; } = string.Empty;

    public void OnGet()
    {
        SuggestedFolder = storage.Value.ImportFolder;
    }

    public async Task<IActionResult> OnPostUploadAsync(
        List<IFormFile>? files, bool overwrite, CancellationToken ct)
    {
        await RunAsync(async () =>
        {
            foreach (var file in files ?? new List<IFormFile>())
            {
                if (file.Length == 0)
                    continue;

                string id = PaperService.SafeId(Path.GetFileNameWithoutExtension(file.FileName));
                if (id.Length == 0)
                {
                    Skip(file.FileName, "the file name has no usable id.");
                    continue;
                }

                await using var stream = file.OpenReadStream();
                var (ok, error, paper) = await papers.ParseAsync(stream, ct);
                if (!ok || paper is null)
                {
                    Skip(file.FileName, error);
                    continue;
                }

                var (saved, saveError) = await papers.SaveAsync(id, paper, overwrite, ct);
                if (saved)
                {
                    Saved++;
                    Results.Add($"Saved {id} from {file.FileName}.");
                }
                else
                {
                    Skip(file.FileName, saveError);
                }
            }
        }, "paper.import.upload");

        return Page();
    }

    public async Task<IActionResult> OnPostPasteAsync(
        string id, string json, bool overwrite, CancellationToken ct)
    {
        await RunAsync(async () =>
        {
            string safe = PaperService.SafeId(id);
            if (safe.Length == 0)
            {
                Skip(id, "the id is not usable.");
                return;
            }

            var (ok, error) = await papers.SaveRawAsync(safe, json, overwrite, ct);
            if (ok)
            {
                Saved++;
                Results.Add($"Saved {safe} from pasted JSON.");
            }
            else
            {
                Skip(safe, error);
            }
        }, "paper.import.paste");

        return Page();
    }

    public async Task<IActionResult> OnPostFolderAsync(
        string folder, bool overwrite, CancellationToken ct)
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                Skip("folder", "no folder was given.");
                return;
            }

            string path = StoragePaths.Resolve(folder);
            if (path.Length == 0 || !Directory.Exists(path))
            {
                Skip(folder, "the folder does not exist.");
                return;
            }

            int imported = await papers.ImportFolderAsync(path, overwrite, ct);
            Saved += imported;
            Results.Add($"Imported {imported} paper(s) from {path}.");
            if (imported == 0)
                Skipped++;
        }, "paper.import.folder");

        return Page();
    }

    private void Skip(string target, string reason)
    {
        Skipped++;
        Results.Add($"Skipped {target}: {reason}");
    }

    private async Task RunAsync(Func<Task> work, string action)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Results.Add($"Stopped: {e.GetType().Name}.");
        }

        await audit.WriteAsync(Actor, action, $"{Saved} saved, {Skipped} skipped",
            string.Empty, Ip, HttpContext.RequestAborted);

        if (Saved > 0 && Skipped == 0)
            TempData["Message"] = $"{Saved} paper(s) imported.";
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
