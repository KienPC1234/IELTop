using System.IO;
using System.Text.Json;
using IELTop.Models;
using IELTop.Services.Exam;
using IELTop.Services.Storage;

namespace IELTop.Services.App;

/// <summary>One row in the server list, community or saved.</summary>
public sealed record ServerRow(string Name, string Url, bool IsSaved)
{
    public string Detail => IsSaved ? "Saved on this computer" : "Community list";
}

/// <summary>One remote paper row with a live downloaded badge.</summary>
public sealed record RemotePaperRow(
    string Id,
    string Title,
    string Summary,
    bool IsDownloaded,
    bool IsUpdateAvailable);

/// <summary>The Servers screen state.</summary>
public sealed class ServersSnapshot
{
    public IReadOnlyList<ServerRow> Servers { get; init; } = Array.Empty<ServerRow>();
    public IReadOnlyList<RemotePaperRow> Papers { get; init; } = Array.Empty<RemotePaperRow>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AuthModes { get; init; } = new[] { "Anonymous", "AccessCode", "Login" };
    public string SelectedServerUrl { get; init; } = string.Empty;
    public string ServerInfo { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string NewUrl { get; init; } = string.Empty;
    public string AuthMode { get; init; } = "Anonymous";
    public string Username { get; init; } = string.Empty;
    public bool HasSecret { get; init; }
    public bool AllowInsecure { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public bool IsBusy { get; init; }
    public string SearchText { get; init; } = string.Empty;
    public string SelectedCategory { get; init; } = "All categories";
    public bool HasPapers { get; init; }
    public bool HasSelection { get; init; }
}

/// <summary>
/// Browse IELTop content servers, download papers, and keep them for Mock Test.
/// Servers can be anonymous, need an access code, or need a login. Secrets are
/// protected on this computer, never stored in plain text.
/// </summary>
public sealed class ServersService
{
    private readonly IContentServerStore _store;
    private readonly IContentServerClient _client;
    private readonly IExamRepository _exams;
    private readonly ExamEngine _exam;

    private readonly List<RemotePaper> _all = new();
    private CancellationTokenSource? _cts;

    private string _selectedUrl = string.Empty;
    private string _serverInfo = string.Empty;
    private string _newName = string.Empty;
    private string _newUrl = string.Empty;
    private string _authMode = "Anonymous";
    private string _username = string.Empty;
    private string _secret = string.Empty;
    private bool _allowInsecure;
    private string _status = "Pick a server, then press Connect.";
    private bool _busy;
    private string _search = string.Empty;
    private string _category = "All categories";

    public ServersService(
        IContentServerStore store,
        IContentServerClient client,
        IExamRepository exams,
        ExamEngine exam)
    {
        _store = store;
        _client = client;
        _exams = exams;
        _exam = exam;
        RefreshServers();
    }

    public ServersSnapshot SetNewName(string v) { _newName = v ?? string.Empty; return Snapshot(); }
    public ServersSnapshot SetNewUrl(string v) { _newUrl = v ?? string.Empty; return Snapshot(); }
    public ServersSnapshot SetAuthMode(string v) { _authMode = v ?? "Anonymous"; return Snapshot(); }
    public ServersSnapshot SetUsername(string v) { _username = v ?? string.Empty; return Snapshot(); }
    public ServersSnapshot SetSecret(string v) { _secret = v ?? string.Empty; return Snapshot(); }
    public ServersSnapshot SetAllowInsecure(bool v) { _allowInsecure = v; return Snapshot(); }
    public ServersSnapshot SetSearch(string v) { _search = v ?? string.Empty; return Snapshot(); }
    public ServersSnapshot SetCategory(string v) { _category = v ?? "All categories"; return Snapshot(); }

    public ServersSnapshot RefreshServers()
    {
        var selected = _selectedUrl;
        var list = new List<ServerRow>();
        foreach (var community in _store.CommunityServers())
            list.Add(new ServerRow(community.Name, community.BaseUrl, false));
        foreach (var saved in _store.SavedServers())
            if (!list.Any(r => string.Equals(r.Url, saved.BaseUrl, StringComparison.OrdinalIgnoreCase)))
                list.Add(new ServerRow(saved.Name, saved.BaseUrl, true));

        _selectedUrl = list.Any(r => string.Equals(r.Url, selected, StringComparison.OrdinalIgnoreCase))
            ? selected
            : string.Empty;
        _servers = list;
        return Snapshot();
    }

    private List<ServerRow> _servers = new();

    public ServersSnapshot SelectServer(string url)
    {
        _selectedUrl = url ?? string.Empty;
        _all.Clear();
        _serverInfo = string.Empty;
        _search = string.Empty;
        _category = "All categories";
        var saved = FindSaved(_selectedUrl);
        if (saved is not null)
        {
            _authMode = saved.AuthMode.ToString();
            _username = saved.Username;
            _allowInsecure = saved.AllowInsecure;
            _secret = string.Empty;
        }
        var row = _servers.FirstOrDefault(r => string.Equals(r.Url, _selectedUrl, StringComparison.OrdinalIgnoreCase));
        if (row is not null) _status = $"Selected {row.Name}. Press Connect to list its papers.";
        return Snapshot();
    }

    public async Task<ServersSnapshot> ConnectAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_selectedUrl))
        {
            _status = "Pick a server first.";
            return Snapshot();
        }
        var credential = await BuildCredentialAsync(_selectedUrl, ct);
        if (credential is null) return Snapshot();

        _busy = true;
        _status = "Connecting.";
        try
        {
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var info = await _client.InfoAsync(_selectedUrl, credential, _cts.Token);
            if (!info.Success || info.Value is null) { _status = info.Error; return Snapshot(); }
            _serverInfo = $"{info.Value.Name}, skills: {string.Join(", ", info.Value.Skills)}.";

            var papers = await _client.ListPapersAsync(_selectedUrl, credential, null, _cts.Token);
            if (!papers.Success || papers.Value is null) { _status = papers.Error; return Snapshot(); }
            _all.Clear();
            _all.AddRange(papers.Value);
            _category = "All categories";
            _status = _all.Count == 0
                ? "Connected, but this server has no papers."
                : $"Connected. {_all.Count} paper(s). Pick one and press Download.";
        }
        catch (OperationCanceledException) { _status = "Connection stopped."; }
        finally { _busy = false; }
        return Snapshot();
    }

    public ServersSnapshot AddServer()
    {
        var name = _newName.Trim();
        var url = _newUrl.Trim().TrimEnd('/');
        if (name.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _status = "Enter a name and an address starting with http.";
            return Snapshot();
        }
        _store.AddOrUpdate(new SavedServer
        {
            Name = name,
            BaseUrl = url,
            AuthMode = ParseAuthMode(_authMode),
            Username = _username.Trim(),
            AllowInsecure = _allowInsecure,
            ProtectedSecret = SecretProtector.Protect(_secret),
        });
        _newName = string.Empty;
        _newUrl = string.Empty;
        _secret = string.Empty;
        RefreshServers();
        _status = $"Saved {name}. Select it and press Connect.";
        return Snapshot();
    }

    public ServersSnapshot RemoveServer()
    {
        var saved = FindSaved(_selectedUrl);
        if (saved is null) { _status = "Community entries cannot be removed."; return Snapshot(); }
        _store.Remove(saved);
        _selectedUrl = string.Empty;
        _all.Clear();
        RefreshServers();
        _status = "Server removed.";
        return Snapshot();
    }

    public ServersSnapshot Cancel()
    {
        _cts?.Cancel();
        return Snapshot();
    }

    public async Task<ServersSnapshot> DownloadAllAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_selectedUrl) || _all.Count == 0)
        {
            _status = "Connect first, so there is a list to download.";
            return Snapshot();
        }
        var credential = await BuildCredentialAsync(_selectedUrl, ct);
        if (credential is null) return Snapshot();

        _busy = true;
        try
        {
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            int ok = 0, failed = 0;
            string? firstError = null;
            var list = Filtered().ToList();
            for (int i = 0; i < list.Count; i++)
            {
                _status = $"Downloading {i + 1} of {list.Count}: {list[i].Title}.";
                var error = await DownloadOneAsync(_selectedUrl, list[i], credential, _cts.Token, silent: true);
                if (error is null) ok++;
                else { failed++; firstError ??= $"{list[i].Title}: {error}"; }
            }
            _exam.Load();
            _status = failed == 0
                ? $"Saved {ok} paper(s). Open Mock Test to run them."
                : $"Saved {ok} paper(s), {failed} failed. First error: {firstError}";
        }
        catch (OperationCanceledException) { _status = "Download stopped."; }
        finally { _busy = false; }
        return Snapshot();
    }

    public async Task<ServersSnapshot> DownloadPaperAsync(string id, CancellationToken ct)
    {
        var remote = _all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(_selectedUrl) || remote is null)
        {
            _status = "Pick a server and a paper first.";
            return Snapshot();
        }
        var credential = await BuildCredentialAsync(_selectedUrl, ct);
        if (credential is null) return Snapshot();

        _busy = true;
        _status = $"Downloading {remote.Title}.";
        try
        {
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var error = await DownloadOneAsync(_selectedUrl, remote, credential, _cts.Token, silent: false);
            if (error is null) _exam.Load();
        }
        catch (OperationCanceledException) { _status = "Download stopped."; }
        finally { _busy = false; }
        return Snapshot();
    }

    private async Task<string?> DownloadOneAsync(
        string baseUrl, RemotePaper remote, ServerCredential credential, CancellationToken ct, bool silent)
    {
        var paper = await _client.DownloadPaperAsync(baseUrl, remote.Id, credential, ct);
        if (!paper.Success || paper.Value is null)
        {
            if (!silent) _status = paper.Error;
            return paper.Error;
        }

        var issues = PaperValidator.Validate(paper.Value);
        if (issues.Count > 0)
        {
            var error = $"{paper.Value.Title} failed validation: {issues[0]}";
            if (!silent) _status = error;
            return error;
        }

        try
        {
            var paperPath = Path.Combine(_exams.ExamsDir, SafeFileName(remote.Id) + ".json");
            File.WriteAllText(paperPath, JsonSerializer.Serialize(paper.Value,
                new JsonSerializerOptions { WriteIndented = true }));

            int audioOk = 0, audioTotal = 0;
            var clips = paper.Value.Parts
                .Where(p => !string.IsNullOrWhiteSpace(p.AudioFile))
                .Select(p => p.AudioFile).Distinct().ToList();
            foreach (var clip in clips)
            {
                audioTotal++;
                if (!silent) _status = $"Downloading clip {audioTotal} of {clips.Count}.";
                var audio = await _client.DownloadAudioAsync(baseUrl, clip, credential, ct);
                if (!audio.Success || audio.Value is null) continue;
                Directory.CreateDirectory(_exams.AudioDir);
                File.WriteAllBytes(Path.Combine(_exams.AudioDir, clip), audio.Value);
                audioOk++;
            }

            if (!silent)
                _status = audioTotal == 0
                    ? $"Saved {paper.Value.Title}. Open Mock Test to run it."
                    : $"Saved {paper.Value.Title} with {audioOk} of {audioTotal} clip(s).";
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            const string error = "Could not save the paper. Check disk space and try again.";
            if (!silent) _status = error;
            return error;
        }
    }

    private async Task<ServerCredential?> BuildCredentialAsync(string url, CancellationToken ct)
    {
        var saved = FindSaved(url);
        var mode = saved?.AuthMode ?? ParseAuthMode(_authMode);
        var username = saved?.Username ?? _username.Trim();
        bool insecure = saved?.AllowInsecure ?? _allowInsecure;
        var secret = saved is not null ? SecretProtector.Unprotect(saved.ProtectedSecret) : _secret;
        var token = saved is not null ? SecretProtector.Unprotect(saved.ProtectedToken) : string.Empty;

        if (mode == ServerAuthMode.Anonymous)
            return new ServerCredential(AllowInsecure: insecure);

        if (mode == ServerAuthMode.AccessCode)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                _status = "Enter the access code for this server.";
                return null;
            }
            return new ServerCredential(AccessCode: secret, AllowInsecure: insecure);
        }

        if (!string.IsNullOrWhiteSpace(token))
            return new ServerCredential(Token: token, AllowInsecure: insecure);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(secret))
        {
            _status = "Enter a username and a password.";
            return null;
        }

        _busy = true;
        _status = "Signing in.";
        try
        {
            var login = await _client.LoginAsync(url, new ServerCredential(AllowInsecure: insecure), username, secret, ct);
            if (!login.Success || string.IsNullOrWhiteSpace(login.Value)) { _status = login.Error; return null; }
            if (saved is not null)
            {
                saved.ProtectedToken = SecretProtector.Protect(login.Value);
                _store.Save();
            }
            return new ServerCredential(Token: login.Value, AllowInsecure: insecure);
        }
        finally { _busy = false; }
    }

    private IEnumerable<RemotePaper> Filtered()
    {
        var query = _search.Trim();
        foreach (var paper in _all)
        {
            if (_category != "All categories"
                && !string.Equals(paper.Category, _category, StringComparison.OrdinalIgnoreCase)) continue;
            if (query.Length > 0 && !paper.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            yield return paper;
        }
    }

    private bool IsDownloaded(string id)
    {
        try { return File.Exists(Path.Combine(_exams.ExamsDir, SafeFileName(id) + ".json")); }
        catch { return false; }
    }

    private bool IsUpdateAvailable(RemotePaper paper)
    {
        try
        {
            var path = Path.Combine(_exams.ExamsDir, SafeFileName(paper.Id) + ".json");
            if (!File.Exists(path)) return false;
            return UpdateChecker.IsNewer(paper.Updated, File.GetLastWriteTime(path));
        }
        catch { return false; }
    }

    private SavedServer? FindSaved(string url) =>
        _store.SavedServers().FirstOrDefault(s =>
            string.Equals(s.BaseUrl, url, StringComparison.OrdinalIgnoreCase));

    private static ServerAuthMode ParseAuthMode(string value) =>
        Enum.TryParse<ServerAuthMode>(value, ignoreCase: true, out var mode) ? mode : ServerAuthMode.Anonymous;

    private static string SafeFileName(string id)
    {
        var clean = new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        clean = string.Join("-", clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length switch { 0 => "paper", > 60 => clean[..60], _ => clean };
    }

    public ServersSnapshot Snapshot()
    {
        var rows = Filtered().Select(p =>
            new RemotePaperRow(p.Id, p.Title, p.Summary, IsDownloaded(p.Id), IsUpdateAvailable(p))).ToList();

        var categories = new List<string> { "All categories" };
        categories.AddRange(_all.Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

        return new ServersSnapshot
        {
            Servers = _servers,
            Papers = rows,
            Categories = categories,
            SelectedServerUrl = _selectedUrl,
            ServerInfo = _serverInfo,
            NewName = _newName,
            NewUrl = _newUrl,
            AuthMode = _authMode,
            Username = _username,
            HasSecret = !string.IsNullOrWhiteSpace(_secret),
            AllowInsecure = _allowInsecure,
            StatusMessage = _status,
            IsBusy = _busy,
            SearchText = _search,
            SelectedCategory = _category,
            HasPapers = rows.Count > 0,
            HasSelection = !string.IsNullOrWhiteSpace(_selectedUrl),
        };
    }
}
