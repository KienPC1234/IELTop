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
    Task<string?> EnsureLocalAsync(string fileName, CancellationToken ct = default);
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
    IS3StorageService s3,
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

    public async Task<string?> EnsureLocalAsync(string fileName, CancellationToken ct = default)
    {
        string? existing = ResolveExisting(fileName);
        if (existing is not null)
            return existing;

        if (!s3.Enabled)
            return null;

        string safe = SafeFileName(fileName);
        if (safe.Length == 0)
            return null;

        Directory.CreateDirectory(Root);
        string localPath = Path.Combine(Root, safe);

        bool downloaded = await s3.DownloadToFileAsync($"audio/{safe}", localPath, ct);
        return downloaded && File.Exists(localPath) ? localPath : null;
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

            // Cap by a real size check rather than in memory, uploads may be large.
            length = new FileInfo(temp).Length;
            long cap = Math.Max(1, _storage.MaxUploadMb) * 1024L * 1024L;
            if (length > cap)
            {
                File.Delete(temp);
                return (false, $"The file is larger than {_storage.MaxUploadMb} MB.");
            }

            // Automatically standardize non-m4a/opus files to .m4a with AAC and faststart
            string ext = Path.GetExtension(safe);
            if (!StandardFormats.Contains(ext))
            {
                string standardizedSafe = Path.GetFileNameWithoutExtension(safe) + ".m4a";
                string transcodeTemp = Path.Combine(Root, standardizedSafe + ".tmp.m4a");
                var (transcodeOk, transcodePath, transcodeErr) = await TranscodeToM4aAsync(temp, transcodeTemp, ct);

                if (transcodeOk && File.Exists(transcodePath))
                {
                    File.Delete(temp);
                    safe = standardizedSafe;
                    target = Path.Combine(Root, safe);
                    File.Move(transcodePath, target, overwrite: true);

                    // Recalculate hash and size for the standardized file
                    var (recomputedHash, recomputedLength) = await ComputeFileHashAndLengthAsync(target, ct);
                    hash = recomputedHash;
                    length = recomputedLength;
                    logger.LogInformation("Audio {Original} standardized to m4a format ({Size} bytes)", fileName, length);
                }
                else
                {
                    logger.LogWarning("Audio standardization failed ({Error}), keeping original format", transcodeErr);
                    File.Move(temp, target, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, target, overwrite: true);
            }
        }
        catch (IOException)
        {
            if (File.Exists(temp)) File.Delete(temp);
            return (false, "The file could not be saved.");
        }

        if (s3.Enabled)
        {
            try
            {
                await using var s3Stream = File.OpenRead(target);
                var (s3Ok, s3Err) = await s3.UploadAsync($"audio/{safe}", s3Stream, ContentTypeFor(safe), ct);
                if (!s3Ok)
                    logger.LogWarning("Failed to upload audio {File} to S3: {Error}", safe, s3Err);
                else
                    logger.LogInformation("Audio {File} synchronized to S3 storage", safe);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync audio {File} to S3", safe);
            }
        }

        if (row is null)
        {
            row = new AudioAsset { FileName = safe };
            db.Audio.Add(row);
        }
        row.ContentType = ContentTypeFor(safe);
        row.Format = Path.GetExtension(safe).TrimStart('.').ToLowerInvariant();
        row.IsStandardized = StandardFormats.Contains(Path.GetExtension(safe));
        row.S3Url = s3.Enabled ? s3.GetPublicUrl($"audio/{safe}") : string.Empty;
        row.SizeBytes = length;
        row.Sha256 = hash;
        row.UploadedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return (true, string.Empty);
    }

    private static readonly HashSet<string> StandardFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4a", ".opus"
    };

    private static string FindFfmpegPath()
    {
        string? env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;
        if (File.Exists("/home/linuxbrew/.linuxbrew/bin/ffmpeg"))
            return "/home/linuxbrew/.linuxbrew/bin/ffmpeg";
        return "ffmpeg";
    }

    private async Task<(bool Ok, string Path, string Error)> TranscodeToM4aAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        string ffmpeg = FindFfmpegPath();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-i \"{sourcePath}\" -vn -c:a aac -b:a 128k -f ipod -movflags +faststart \"{destPath}\" -y",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
                return (false, string.Empty, "Could not start ffmpeg process.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

            await proc.WaitForExitAsync(timeoutCts.Token);
            if (proc.ExitCode == 0 && File.Exists(destPath) && new FileInfo(destPath).Length > 0)
            {
                return (true, destPath, string.Empty);
            }

            string err = await proc.StandardError.ReadToEndAsync(ct);
            return (false, string.Empty, $"Transcoding failed: {err}");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"Transcoding error: {ex.Message}");
        }
    }

    private static async Task<(string Hash, long Length)> ComputeFileHashAndLengthAsync(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(path);
        byte[] hashBytes = await sha.ComputeHashAsync(stream, ct);
        long length = new FileInfo(path).Length;
        return (Convert.ToHexString(hashBytes).ToLowerInvariant(), length);
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

        if (s3.Enabled)
        {
            try
            {
                await s3.DeleteAsync($"audio/{safe}", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete audio {File} from S3", safe);
            }
        }

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
