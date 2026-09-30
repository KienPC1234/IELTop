using System.Text.Json;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Editable runtime settings that override appsettings.json. Changing
/// one bumps the content version so cached greetings pick it up.
/// </summary>
public interface ISettingsService
{
    Task<Dictionary<string, string>> AllAsync(CancellationToken ct = default);
    Task<ServerSettings> GetServerSettingsAsync(CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task SetManyAsync(IDictionary<string, string> values, CancellationToken ct = default);
    Task<bool> LoginEnabledAsync(CancellationToken ct = default);
}

/// <summary>
/// The subset of settings the editor writes.
/// </summary>
public sealed class ServerSettings
{
    public string Name { get; set; } = "IELTop Content Server";
    public bool AllowAnonymous { get; set; }
    public bool AllowAccessCode { get; set; } = true;
    public bool AllowLogin { get; set; } = true;
    public List<string> Skills { get; set; } = new();
}

public sealed class SettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IContentCache cache,
    IOptions<ServerOptions> serverOptions) : ISettingsService
{
    private readonly ServerOptions _server = serverOptions.Value;

    public async Task<Dictionary<string, string>> AllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Settings.AsNoTracking().ToListAsync(ct);
        return rows
            .Where(r => !r.Key.StartsWith("content.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ServerSettings> GetServerSettingsAsync(CancellationToken ct = default)
    {
        var map = await AllAsync(ct);
        return new ServerSettings
        {
            Name = Value(map, "server.name", _server.Name),
            AllowAnonymous = Flag(map, "auth.anonymous", _server.AllowAnonymous),
            AllowAccessCode = Flag(map, "auth.code", _server.AllowAccessCode),
            AllowLogin = Flag(map, "auth.login", _server.AllowLogin),
            Skills = ParseSkills(map.GetValueOrDefault("server.skills"),
                _server.Skills.Count > 0 ? _server.Skills : ServerOptions.DefaultSkills)
        };
    }

    public async Task<bool> LoginEnabledAsync(CancellationToken ct = default)
        => (await GetServerSettingsAsync(ct)).AllowLogin;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            row.Value = value;
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
    }

    public async Task SetManyAsync(IDictionary<string, string> values, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row is null)
                db.Settings.Add(new Setting { Key = key, Value = value });
            else
                row.Value = value;
        }
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
    }

    private static string Value(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool Flag(Dictionary<string, string> map, string key, bool fallback) =>
        map.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static List<string> ParseSkills(string? raw, List<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(raw);
            return list is { Count: > 0 } ? list : fallback;
        }
        catch (JsonException)
        {
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() is { Count: > 0 } parts
                ? parts
                : fallback;
        }
    }
}
