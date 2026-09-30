using System.Security.Cryptography;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Audio clips live as files under Storage/Audio with one row of
/// metadata in the database. Serving uses the file directly so range
/// requests and kernel sendfile work without a copy in memory.
/// </summary>
public interface IAudioService
{
    string Root { get; }
    string? ResolveExisting(string fileName);
    string ContentTypeFor(string fileName);
    Task<List<AudioAsset>> ListAsync(CancellationToken ct = default);
    Task<(bool Ok, string Error)> ImportFileAsync(string sourcePath, bool overwrite, CancellationToken ct = default);
    Task<(bool Ok, string Error)> ImportFolderAsync(string folder, bool overwrite, CancellationToken ct = default);
    Task<(bool Ok, string Error)> SaveAsync(string fileName, Stream upload, bool overwrite, CancellationToken ct = default);
    Task<bool> DeleteAsync(string fileName, CancellationToken ct = default);
    Task RecordDownloadAsync(string fileName, CancellationToken ct = default);
    Task<HashSet<string>> NamesAsync(CancellationToken ct = default);
}

public sealed class AudioService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWriteThrottle throttle,
    IOptions<StorageOptions> storage,
    ILogger<AudioService> logger) : IAudioService
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".flac"] = "audio/flac",
        [".aac"] = "audio/aac",
        [".opus"] = "audio/opus"
    };

    private static readonly string[] Allowed = Types.Keys.ToArray();

    private readonly StorageOptions _storage = storage.Value;

    public string Root => Path.Combine(StoragePaths.Root(_storage), "Audio");

    public static bool IsAudio(string path) =>
        Allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public string ContentTypeFor(string fileName) =>
        Types.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

    public string? ResolveExisting(string fileName)
    {
        string safe = SafeFileName(fileName);
        if (safe.Length == 0)
            return null;
        string full = Path.GetFullPath(Path.Combine(Root, safe));
        string root = Path.GetFullPath(Root);
        if (!IsInside(root, full))
            return null;
        return File.Exists(full) ? full : null;
    }

    private static bool IsInside(string root, string full)
    {
        string withSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return full.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<AudioAsset>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Audio
            .OrderBy(a => a.FileName)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<HashSet<string>> NamesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var names = await db.Audio.Select(a => a.FileName).AsNoTracking().ToListAsync(ct);
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(bool Ok, string Error)> ImportFolderAsync(string folder, bool overwrite, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder))
            return (false, "The folder does not exist.");

        int ok = 0, skipped = 0;
        foreach (var path in Directory.EnumerateFiles(folder).OrderBy(p => p))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsAudio(path))
                continue;
            var (saved, _) = await ImportFileAsync(path, overwrite, ct);
            if (saved) ok++;
            else skipped++;
        }

        logger.LogInformation("Audio import from {Folder}: {Ok} saved, {Skipped} skipped", folder, ok, skipped);
        return ok > 0 || skipped == 0
            ? (true, string.Empty)
            : (false, "No audio files were imported.");
    }

    public async Task<(bool Ok, string Error)> ImportFileAsync(string sourcePath, bool overwrite, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            return (false, "The file does not exist.");
        if (!IsAudio(sourcePath))
            return (false, "Only wav, mp3, m4a, ogg, flac, aac, or opus files are supported.");

        await using var stream = File.OpenRead(sourcePath);
        return await SaveAsync(Path.GetFileName(sourcePath), stream, overwrite, ct);
    }

    public async Task<(bool Ok, string Error)> SaveAsync(
        string fileName, Stream upload, bool overwrite, CancellationToken ct = default)
    {
        string safe = SafeFileName(fileName);
        if (safe.Length == 0)
            return (false, "The file name is not usable.");
        if (!IsAudio(safe))
            return (false, "Only wav, mp3, m4a, ogg, flac, aac, or opus files are supported.");

        Directory.CreateDirectory(Root);
        string target = Path.Combine(Root, safe);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Audio.FirstOrDefaultAsync(a => a.FileName == safe, ct);
        if (row is not null && !overwrite)
            return (false, "A clip with this name already exists.");

        // Write to a temp file first, so a broken upload never replaces
        // a working clip.
        string temp = target + ".uploading";
        string hash;
        long length;
        try
        {
            await using (var file = File.Create(temp))
            {
                using var sha = SHA256.Create();
                var buffer = new byte[81920];
                int read;
                while ((read = await upload.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
            }

            // Cap by a real size check rather than in memory, uploads
            // may be large.
            length = new FileInfo(temp).Length;
            long cap = Math.Max(1, _storage.MaxUploadMb) * 1024L * 1024L;
            if (length > cap)
            {
                File.Delete(temp);
                return (false, $"The file is larger than {_storage.MaxUploadMb} MB.");
            }

            File.Move(temp, target, overwrite: true);
        }
        catch (IOException)
        {
            if (File.Exists(temp)) File.Delete(temp);
            return (false, "The file could not be saved.");
        }

        if (row is null)
        {
            row = new AudioAsset { FileName = safe };
            db.Audio.Add(row);
        }
        row.ContentType = ContentTypeFor(safe);
        row.SizeBytes = length;
        row.Sha256 = hash;
        row.UploadedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return (true, string.Empty);
    }

    public async Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
    {
        string safe = SafeFileName(fileName);
        if (safe.Length == 0)
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Audio.FirstOrDefaultAsync(a => a.FileName == safe, ct);
        if (row is null)
            return false;

        db.Audio.Remove(row);
        await db.SaveChangesAsync(ct);

        string? full = ResolveExisting(safe);
        if (full is not null)
        {
            try
            {
                File.Delete(full);
            }
            catch (IOException)
            {
                logger.LogWarning("Deleted row but could not remove {File}", full);
            }
        }
        return true;
    }

    public async Task RecordDownloadAsync(string fileName, CancellationToken ct = default)
    {
        if (!throttle.ShouldWrite($"audio.dl:{fileName}", TimeSpan.FromMinutes(1)))
            return;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Audio
                .Where(a => a.FileName == fileName)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DownloadCount, a => a.DownloadCount + 1), ct);
        }
        catch (Exception)
        {
            // Counting is best effort.
        }
    }

    /// <summary>
    /// Strip any directory part and reject characters that a file
    /// system or a url would treat specially.
    /// </summary>
    public static string SafeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string name = Path.GetFileName(raw.Trim());
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ')
                builder.Append(c);
        }
        name = builder.ToString().Trim().TrimStart('.');
        return name.Length > 200 ? name[..200] : name;
    }
}

/// <summary>
/// Central place for the on disk layout so nothing else joins paths.
/// </summary>
public static class StoragePaths
{
    public static string Root(StorageOptions options)
    {
        string root = options.Root;
        if (string.IsNullOrWhiteSpace(root))
            root = "App_Data";
        return Path.GetFullPath(root);
    }

    /// <summary>
    /// Turns a configured folder into a full path. A relative path is
    /// tried against the working directory first (what a developer
    /// expects when running from the project) and the app directory
    /// second (what a deployed service expects).
    /// </summary>
    public static string Resolve(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return string.Empty;
        if (Path.IsPathRooted(folder))
            return Path.GetFullPath(folder);

        string fromWorkingDir = Path.GetFullPath(folder);
        if (Directory.Exists(fromWorkingDir))
            return fromWorkingDir;

        string fromApp = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, folder));
        return Directory.Exists(fromApp) ? fromApp : fromWorkingDir;
    }

    public static string Papers(StorageOptions options) => Path.Combine(Root(options), "Papers");
    public static string Audio(StorageOptions options) => Path.Combine(Root(options), "Audio");
    public static string Uploads(StorageOptions options) => Path.Combine(Root(options), "Uploads");
    public static string DataProtection(StorageOptions options) => Path.Combine(Root(options), "Keys");
}
