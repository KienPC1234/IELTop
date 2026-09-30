using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server;

/// <summary>
/// Creates the store on first run, seeds the admin, the default
/// settings, and the shipped sample content. Everything here is safe to
/// run on every start.
/// </summary>
public sealed class DbInitializer(
    IDbContextFactory<AppDbContext> dbFactory,
    IAdminAuthService admins,
    IPaperService papers,
    IAudioService audio,
    IContentCache cache,
    IOptions<ServerOptions> serverOptions,
    IOptions<StorageOptions> storageOptions,
    ILogger<DbInitializer> logger)
{
    private readonly ServerOptions _server = serverOptions.Value;
    private readonly StorageOptions _storage = storageOptions.Value;

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.Database.EnsureCreatedAsync(ct);
        }

        await admins.EnsureSeedAsync(ct);
        await SeedSettingsAsync(ct);
        await SeedContentAsync(ct);
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["server.name"] = _server.Name,
            ["server.skills"] = System.Text.Json.JsonSerializer.Serialize(
                _server.Skills.Count > 0 ? _server.Skills : ServerOptions.DefaultSkills),
            ["auth.anonymous"] = _server.AllowAnonymous.ToString(),
            ["auth.code"] = _server.AllowAccessCode.ToString(),
            ["auth.login"] = _server.AllowLogin.ToString(),
            ["content.version"] = "1",
            ["setup.completed"] = "true"
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        var missing = defaults.Where(kv => !existing.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0)
            return;

        foreach (var (key, value) in missing)
            db.Settings.Add(new Setting { Key = key, Value = value });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedContentAsync(CancellationToken ct)
    {
        int paperCount = await papers.CountAsync(ct);
        if (paperCount == 0 && !string.IsNullOrWhiteSpace(_storage.SeedFolder))
        {
            string folder = StoragePaths.Resolve(_storage.SeedFolder);
            if (Directory.Exists(folder))
            {
                int imported = await papers.ImportFolderAsync(folder, overwrite: true, ct);
                logger.LogInformation("Seeded {Count} papers from {Folder}", imported, folder);
            }
            else
            {
                logger.LogInformation("Seed folder {Folder} does not exist, skipping.", folder);
            }
        }

        var names = await audio.NamesAsync(ct);
        if (names.Count == 0 && !string.IsNullOrWhiteSpace(_storage.SeedFolder))
        {
            string folder = StoragePaths.Resolve(_storage.SeedFolder);
            if (Directory.Exists(folder))
                await audio.ImportFolderAsync(folder, overwrite: false, ct);
        }

        await cache.BumpVersionAsync(ct);
    }
}
