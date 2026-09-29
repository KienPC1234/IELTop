using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>One row in the server list, community or saved.</summary>
public sealed partial class ServerRow : ObservableObject
{
    public string Name { get; }
    public string Url { get; }
    public bool IsSaved { get; }

    [ObservableProperty] private bool _isSelected;

    public ServerRow(string name, string url, bool isSaved)
    {
        Name = name;
        Url = url;
        IsSaved = isSaved;
    }

    public string Detail => IsSaved ? "Saved on this computer" : "Community list";
}

/// <summary>One remote paper row with a live downloaded badge.</summary>
public sealed partial class RemotePaperRow : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Summary { get; }

    [ObservableProperty] private bool _isDownloaded;

    [ObservableProperty] private bool _isUpdateAvailable;

    public RemotePaperRow(Services.Storage.RemotePaper paper, bool downloaded)
    {
        Id = paper.Id;
        Title = paper.Title;
        Summary = paper.Summary;
        _isDownloaded = downloaded;
    }
}

/// <summary>
/// Browse IELTop content servers, download papers, and keep them for
/// Mock Test. Servers can be anonymous, need an access code, or need a
/// login. Secrets stay protected on this computer.
/// </summary>
public sealed partial class ServersViewModel : ObservableObject
{
    private readonly Services.Storage.IContentServerStore _store;
    private readonly Services.Storage.IContentServerClient _client;
    private readonly Services.Storage.IExamRepository _exams;
    private readonly ExamViewModel _exam;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private ServerRow? _selectedServer;
    [ObservableProperty] private string _serverInfo = string.Empty;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newUrl = string.Empty;
    [ObservableProperty] private ServerAuthMode _selectedAuthMode = ServerAuthMode.Anonymous;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _secret = string.Empty;
    [ObservableProperty] private bool _allowInsecure;
    [ObservableProperty] private string _statusMessage = "Pick a server, then press Connect.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private RemotePaperRow? _selectedPaper;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All categories";

    private readonly List<Services.Storage.RemotePaper> _allPapers = new();

    public ObservableCollection<ServerRow> Servers { get; } = new();
    public ObservableCollection<RemotePaperRow> Papers { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public IReadOnlyList<ServerAuthMode> AuthModes { get; } =
        new[] { ServerAuthMode.Anonymous, ServerAuthMode.AccessCode, ServerAuthMode.Login };

    /// <summary>True when this paper is already saved for Mock Test.</summary>
    public bool IsDownloaded(string id)
    {
        try
        {
            return File.Exists(Path.Combine(_exams.ExamsDir, SafeFileName(id) + ".json"));
        }
        catch
        {
            return false;
        }
    }

    public ServersViewModel(
        Services.Storage.IContentServerStore store,
        Services.Storage.IContentServerClient client,
        Services.Storage.IExamRepository exams,
        ExamViewModel exam)
    {
        _store = store;
        _client = client;
        _exams = exams;
        _exam = exam;
        RefreshServers();
    }

    public bool HasPapers => Papers.Count > 0;
    public bool HasSelection => SelectedServer is not null;

    partial void OnSelectedServerChanged(ServerRow? value)
    {
        foreach (var row in Servers)
            row.IsSelected = row == value;
        Papers.Clear();
        _allPapers.Clear();
        Categories.Clear();
        Categories.Add("All categories");
        SelectedCategory = "All categories";
        SearchText = string.Empty;
        OnPropertyChanged(nameof(HasPapers));
        ServerInfo = string.Empty;
        if (value is null) return;
        StatusMessage = $"Selected {value.Name}. Press Connect to list its papers.";
        var saved = FindSaved(value.Url);
        if (saved is not null)
        {
            SelectedAuthMode = saved.AuthMode;
            Username = saved.Username;
            AllowInsecure = saved.AllowInsecure;
            Secret = string.Empty;
        }
    }

    [RelayCommand]
    private void SelectServer(ServerRow? row)
    {
        if (row is not null)
            SelectedServer = row;
    }

    [RelayCommand]
    private async Task ConnectToAsync(ServerRow? row)
    {
        if (row is not null)
            SelectedServer = row;
        await ConnectAsync();
    }

    [RelayCommand]
    private void RefreshServers()
    {
        var selected = SelectedServer?.Url;
        Servers.Clear();
        foreach (var community in _store.CommunityServers())
            Servers.Add(new ServerRow(community.Name, community.BaseUrl, false));
        foreach (var saved in _store.SavedServers())
            if (!Servers.Any(r => string.Equals(r.Url, saved.BaseUrl, StringComparison.OrdinalIgnoreCase)))
                Servers.Add(new ServerRow(saved.Name, saved.BaseUrl, true));
        SelectedServer = Servers.FirstOrDefault(r =>
            string.Equals(r.Url, selected, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedServer is null)
        {
            StatusMessage = "Pick a server first.";
            return;
        }
        var credential = await BuildCredentialAsync(SelectedServer.Url);
        if (credential is null) return;

        IsBusy = true;
        StatusMessage = $"Connecting to {SelectedServer.Name}.";
        try
        {
            using var cts = _cts = new CancellationTokenSource();
            var info = await _client.InfoAsync(SelectedServer.Url, credential, cts.Token);
            if (!info.Success || info.Value is null)
            {
                StatusMessage = info.Error;
                return;
            }
            ServerInfo = $"{info.Value.Name}, skills: {string.Join(", ", info.Value.Skills)}.";

            var papers = await _client.ListPapersAsync(SelectedServer.Url, credential, null, cts.Token);
            if (!papers.Success || papers.Value is null)
            {
                StatusMessage = papers.Error;
                return;
            }
            _allPapers.Clear();
            _allPapers.AddRange(papers.Value);
            Categories.Clear();
            Categories.Add("All categories");
            foreach (var category in _allPapers
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                Categories.Add(category);
            SelectedCategory = "All categories";
            ApplyFilter();
            StatusMessage = _allPapers.Count == 0
                ? "Connected, but this server has no papers."
                : $"Connected. {_allPapers.Count} paper(s). Pick one and press Download.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    /// <summary>Client side search and category filter over the last fetch.</summary>
    private void ApplyFilter()
    {
        var selectedId = SelectedPaper?.Id;
        Papers.Clear();
        var query = SearchText.Trim();
        foreach (var paper in _allPapers)
        {
            if (SelectedCategory != "All categories" && !string.Equals(
                paper.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
                continue;
            if (query.Length > 0 && !paper.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            var row = new RemotePaperRow(paper, IsDownloaded(paper.Id));
            row.IsUpdateAvailable = IsUpdateAvailable(paper);
            Papers.Add(row);
        }
        SelectedPaper = Papers.FirstOrDefault(r => r.Id == selectedId);
        OnPropertyChanged(nameof(HasPapers));
    }

    /// <summary>True when the server copy is newer than the saved file.</summary>
    private bool IsUpdateAvailable(Services.Storage.RemotePaper paper)
    {
        try
        {
            var path = Path.Combine(_exams.ExamsDir, SafeFileName(paper.Id) + ".json");
            if (!File.Exists(path)) return false;
            return UpdateChecker.IsNewer(paper.Updated, File.GetLastWriteTime(path));
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        var name = NewName.Trim();
        var url = NewUrl.Trim().TrimEnd('/');
        if (name.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Enter a name and an address starting with http.";
            return;
        }
        _store.AddOrUpdate(new Services.Storage.SavedServer
        {
            Name = name,
            BaseUrl = url,
            AuthMode = SelectedAuthMode,
            Username = Username.Trim(),
            AllowInsecure = AllowInsecure,
            ProtectedSecret = Services.Storage.SecretProtector.Protect(Secret),
        });
        NewName = string.Empty;
        NewUrl = string.Empty;
        Secret = string.Empty;
        RefreshServers();
        StatusMessage = $"Saved {name}. Select it and press Connect.";
    }

    [RelayCommand]
    private void RemoveServer()
    {
        if (SelectedServer is null) return;
        var saved = FindSaved(SelectedServer.Url);
        if (saved is null)
        {
            StatusMessage = "Community entries cannot be removed.";
            return;
        }
        _store.Remove(saved);
        SelectedServer = null;
        Papers.Clear();
        OnPropertyChanged(nameof(HasPapers));
        RefreshServers();
        StatusMessage = "Server removed.";
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        if (SelectedServer is null || Papers.Count == 0)
        {
            StatusMessage = "Connect first, so there is a list to download.";
            return;
        }
        var credential = await BuildCredentialAsync(SelectedServer.Url);
        if (credential is null) return;

        IsBusy = true;
        try
        {
            using var cts = _cts = new CancellationTokenSource();
            int ok = 0, failed = 0;
            string? firstError = null;
            for (int i = 0; i < Papers.Count; i++)
            {
                var row = Papers[i];
                StatusMessage = $"Downloading {i + 1} of {Papers.Count}: {row.Title}.";
                var error = await DownloadOneAsync(
                    SelectedServer.Url, row, credential, cts.Token, silent: true);
                if (error is null) ok++;
                else { failed++; firstError ??= $"{row.Title}: {error}"; }
            }
            _exam.Load();
            StatusMessage = failed == 0
                ? $"Saved {ok} paper(s). Open Mock Test to run them."
                : $"Saved {ok} paper(s), {failed} failed. First error: {firstError}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download stopped.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Downloads and saves one paper plus its clips.
    /// Returns null on success, or a short error otherwise.
    /// </summary>
    private async Task<string?> DownloadOneAsync(
        string baseUrl,
        RemotePaperRow remote,
        Services.Storage.ServerCredential credential,
        CancellationToken ct,
        bool silent)
    {
        var paper = await _client.DownloadPaperAsync(baseUrl, remote.Id, credential, ct);
        if (!paper.Success || paper.Value is null)
        {
            if (!silent) StatusMessage = paper.Error;
            return paper.Error;
        }

        var issues = Services.Storage.PaperValidator.Validate(paper.Value);
        if (issues.Count > 0)
        {
            var error = $"{paper.Value.Title} failed validation: {issues[0]}";
            if (!silent) StatusMessage = error;
            return error;
        }

        try
        {
            var fileName = SafeFileName(remote.Id) + ".json";
            var paperPath = Path.Combine(_exams.ExamsDir, fileName);
            File.WriteAllText(paperPath, JsonSerializer.Serialize(paper.Value,
                new JsonSerializerOptions { WriteIndented = true }));

            int audioOk = 0, audioTotal = 0;
            var clips = paper.Value.Parts
                .Where(p => !string.IsNullOrWhiteSpace(p.AudioFile))
                .Select(p => p.AudioFile)
                .Distinct()
                .ToList();
            foreach (var clip in clips)
            {
                audioTotal++;
                if (!silent) StatusMessage = $"Downloading clip {audioTotal} of {clips.Count}.";
                var audio = await _client.DownloadAudioAsync(baseUrl, clip, credential, ct);
                if (!audio.Success || audio.Value is null) continue;
                Directory.CreateDirectory(_exams.AudioDir);
                File.WriteAllBytes(Path.Combine(_exams.AudioDir, clip), audio.Value);
                audioOk++;
            }

            remote.IsDownloaded = true;
            remote.IsUpdateAvailable = false;
            if (!silent)
                StatusMessage = audioTotal == 0
                    ? $"Saved {paper.Value.Title}. Open Mock Test to run it."
                    : $"Saved {paper.Value.Title} with {audioOk} of {audioTotal} clip(s). Open Mock Test to run it.";
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            const string error = "Could not save the paper. Check disk space and try again.";
            if (!silent) StatusMessage = error;
            return error;
        }
    }

    [RelayCommand]
    private async Task DownloadPaperAsync(RemotePaperRow? paperArg)
    {
        var remote = paperArg ?? SelectedPaper;
        if (SelectedServer is null || remote is null)
        {
            StatusMessage = "Pick a server and a paper first.";
            return;
        }
        var credential = await BuildCredentialAsync(SelectedServer.Url);
        if (credential is null) return;

        IsBusy = true;
        StatusMessage = $"Downloading {remote.Title}.";
        try
        {
            using var cts = _cts = new CancellationTokenSource();
            var error = await DownloadOneAsync(
                SelectedServer.Url, remote, credential, cts.Token, silent: false);
            if (error is null)
                _exam.Load();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download stopped.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Builds the credential for a server. Login mode signs in first and
    /// keeps the token protected for next time.
    /// </summary>
    private async Task<Services.Storage.ServerCredential?> BuildCredentialAsync(string url)
    {
        var saved = FindSaved(url);
        var mode = saved?.AuthMode ?? SelectedAuthMode;
        var username = saved?.Username ?? Username.Trim();
        var insecure = saved?.AllowInsecure ?? AllowInsecure;
        var secret = saved is not null
            ? Services.Storage.SecretProtector.Unprotect(saved.ProtectedSecret)
            : Secret;
        var token = saved is not null
            ? Services.Storage.SecretProtector.Unprotect(saved.ProtectedToken)
            : string.Empty;

        if (mode == ServerAuthMode.Anonymous)
            return new Services.Storage.ServerCredential(AllowInsecure: insecure);

        if (mode == ServerAuthMode.AccessCode)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                StatusMessage = "Enter the access code for this server.";
                return null;
            }
            return new Services.Storage.ServerCredential(AccessCode: secret, AllowInsecure: insecure);
        }

        if (!string.IsNullOrWhiteSpace(token))
            return new Services.Storage.ServerCredential(Token: token, AllowInsecure: insecure);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(secret))
        {
            StatusMessage = "Enter a username and a password.";
            return null;
        }

        IsBusy = true;
        StatusMessage = "Signing in.";
        try
        {
            using var cts = _cts = new CancellationTokenSource();
            var probe = new Services.Storage.ServerCredential(AllowInsecure: insecure);
            var login = await _client.LoginAsync(url, probe, username, secret, cts.Token);
            if (!login.Success || string.IsNullOrWhiteSpace(login.Value))
            {
                StatusMessage = login.Error;
                return null;
            }
            if (saved is not null)
            {
                saved.ProtectedToken = Services.Storage.SecretProtector.Protect(login.Value);
                _store.Save();
            }
            return new Services.Storage.ServerCredential(Token: login.Value, AllowInsecure: insecure);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Services.Storage.SavedServer? FindSaved(string url) =>
        _store.SavedServers().FirstOrDefault(s =>
            string.Equals(s.BaseUrl, url, StringComparison.OrdinalIgnoreCase));

    private static string SafeFileName(string id)
    {
        var clean = new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        clean = string.Join("-", clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length switch
        {
            0 => "paper",
            > 60 => clean[..60],
            _ => clean
        };
    }
}
