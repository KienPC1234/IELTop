using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages.Audio;

[EnableRateLimiting("form")]
public sealed class IndexModel(
    IAudioService audio,
    IAuditService audit,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<StorageOptions> storage) : PageModel
{
    public List<AudioAsset> Clips { get; private set; } = new();
    public HashSet<string> MissingFromDisk { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> UnusedClips { get; private set; } = new();
    public List<string> Results { get; private set; } = new();
    public string SuggestedFolder { get; private set; } = string.Empty;
    public int MaxUploadMb => storage.Value.MaxUploadMb;
    public string TotalSize { get; private set; } = "0 B";

    public async Task OnGetAsync(CancellationToken ct)
    {
        SuggestedFolder = storage.Value.ImportFolder;
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Clips = await audio.ListAsync(ct);
        TotalSize = Size(Clips.Sum(c => c.SizeBytes));

        MissingFromDisk = new HashSet<string>(
            Clips.Where(c => audio.ResolveExisting(c.FileName) is null).Select(c => c.FileName),
            StringComparer.OrdinalIgnoreCase);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Papers.Select(p => p.AudioFilesJson).AsNoTracking().ToListAsync(ct);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in rows)
            foreach (var name in PaperService.Deserialize(json))
                used.Add(name);

        UnusedClips = Clips
            .Where(c => !used.Contains(c.FileName))
            .Select(c => c.FileName)
            .OrderBy(n => n)
            .ToList();
    }

    public string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    public IActionResult OnGetPlay(string file, bool download = false)
    {
        string? path = audio.ResolveExisting(file);
        if (path is null)
            return NotFound();

        string safe = AudioService.SafeFileName(file);
        if (download)
            return PhysicalFile(path, "application/octet-stream", safe);
        return new FileStreamResult(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true),
            audio.ContentTypeFor(safe))
        {
            EnableRangeProcessing = true
        };
    }

    public async Task<IActionResult> OnPostUploadAsync(
        List<IFormFile>? files, bool overwrite, CancellationToken ct)
    {
        long cap = Math.Max(1, storage.Value.MaxUploadMb) * 1024L * 1024L;
        foreach (var file in files ?? new List<IFormFile>())
        {
            if (file.Length == 0)
                continue;
            if (file.Length > cap)
            {
                Results.Add($"Skipped {file.FileName}: larger than {MaxUploadMb} MB.");
                continue;
            }

            await using var stream = file.OpenReadStream();
            var (ok, error) = await audio.SaveAsync(file.FileName, stream, overwrite, ct);
            Results.Add(ok ? $"Saved {file.FileName}." : $"Skipped {file.FileName}: {error}");
        }

        await audit.WriteAsync(Actor, "audio.upload", $"{Results.Count} file(s)", string.Empty, Ip, ct);
        if (Results.Count > 0 && Results.All(r => r.StartsWith("Saved", StringComparison.Ordinal)))
            TempData["Message"] = $"Uploaded {Results.Count} clip(s).";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostFolderAsync(string folder, bool overwrite, CancellationToken ct)
    {
        string path = StoragePaths.Resolve(folder ?? string.Empty);
        if (path.Length == 0 || !Directory.Exists(path))
        {
            TempData["Error"] = "The folder does not exist.";
            await LoadAsync(ct);
            return Page();
        }

        var (ok, error) = await audio.ImportFolderAsync(path, overwrite, ct);
        if (ok)
            TempData["Message"] = $"Imported clips from {path}.";
        else
            TempData["Error"] = error;

        await audit.WriteAsync(Actor, "audio.import.folder", path, error, Ip, ct);
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string file, CancellationToken ct)
    {
        bool removed = await audio.DeleteAsync(file, ct);
        await audit.WriteAsync(Actor, "audio.delete", file, removed ? "ok" : "missing", Ip, ct);
        TempData[removed ? "Message" : "Error"] = removed
            ? $"Deleted {file}."
            : "That clip no longer exists.";
        return RedirectToPage();
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
