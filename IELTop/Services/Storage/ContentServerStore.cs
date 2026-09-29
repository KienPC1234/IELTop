using System.IO;
using System.Text.Json;

namespace IELTop.Services.Storage;

/// <summary>
/// Community servers ship in servers.txt. Servers the user adds live in
/// their profile, with secrets protected by DPAPI, never plain text.
/// </summary>
public interface IContentServerStore
{
    IReadOnlyList<CommunityServer> CommunityServers();
    IReadOnlyList<SavedServer> SavedServers();
    void AddOrUpdate(SavedServer server);
    void Remove(SavedServer server);
    void Save();
}

public sealed class ContentServerStore : IContentServerStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<SavedServer> _saved = new();

    public ContentServerStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "servers.json");
        Load();
    }

    public static string ShippedListPath =>
        Path.Combine(AppContext.BaseDirectory, "servers.txt");

    public IReadOnlyList<CommunityServer> CommunityServers()
    {
        var list = new List<CommunityServer>();
        string path = ShippedListPath;
        if (!File.Exists(path)) return list;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cut = line.Split('|', 2);
            if (cut.Length != 2) continue;
            var name = cut[0].Trim();
            var url = cut[1].Trim().TrimEnd('/');
            if (name.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(new CommunityServer(name, url));
        }
        return list;
    }

    public IReadOnlyList<SavedServer> SavedServers() => _saved.ToList();

    public void AddOrUpdate(SavedServer server)
    {
        var existing = _saved.FirstOrDefault(s =>
            string.Equals(s.BaseUrl, server.BaseUrl, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _saved.Add(server);
        }
        else
        {
            existing.Name = server.Name;
            existing.AuthMode = server.AuthMode;
            existing.Username = server.Username;
            existing.AllowInsecure = server.AllowInsecure;
            existing.ProtectedSecret = server.ProtectedSecret;
            existing.ProtectedToken = server.ProtectedToken;
        }
        Save();
    }

    public void Remove(SavedServer server)
    {
        _saved.RemoveAll(s =>
            string.Equals(s.BaseUrl, server.BaseUrl, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_saved, Options));
        }
        catch (Exception)
        {
            // A save failure must not break browsing. It surfaces on next load.
        }
    }

    private void Load()
    {
        _saved.Clear();
        if (!File.Exists(_path)) return;
        try
        {
            var items = JsonSerializer.Deserialize<List<SavedServer>>(
                File.ReadAllText(_path), Options);
            if (items is not null)
                _saved.AddRange(items.Where(s => !string.IsNullOrWhiteSpace(s.BaseUrl)));
        }
        catch (JsonException)
        {
            // A broken file starts fresh instead of blocking the page.
        }
    }
}
