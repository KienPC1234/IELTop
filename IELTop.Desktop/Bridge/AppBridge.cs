using System.Diagnostics;
using System.Text.Json;
using IELTop.Services.Ai;
using IELTop.Services.App;
using IELTop.Services.Storage;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// Exposes the Library, Editor, Results, Servers, and Settings services to the
/// web UI. Every action returns the fresh snapshot for its page, so a call both
/// acts and refreshes. Slow work is async and never blocks the window.
/// </summary>
public sealed class AppBridge
{
    private readonly LibraryService _library;
    private readonly EditorService _editor;
    private readonly ResultsService _results;
    private readonly ServersService _servers;
    private readonly SettingsService _settings;

    public AppBridge(
        LibraryService library,
        EditorService editor,
        ResultsService results,
        ServersService servers,
        SettingsService settings)
    {
        _library = library;
        _editor = editor;
        _results = results;
        _servers = servers;
        _settings = settings;
    }

    public void Register(BridgeRouter router)
    {
        // ---- Library ----
        router.Register("library.snapshot", Act(_ => _library.Snapshot()));
        router.Register("library.reload", Act(_ => _library.Load()));
        router.Register("library.clearFilters", Act(_ => _library.ClearFilters()));
        router.Register("library.setSearch", Act(a => _library.SetSearch(Str(a, "value"))));
        router.Register("library.setCategory", Act(a => _library.SetCategory(Str(a, "value"))));
        router.Register("library.setSkill", Act(a => _library.SetSkill(Str(a, "value"))));
        router.Register("library.setDraftTitle", Act(a => _library.SetDraftTitle(Str(a, "value"))));
        router.Register("library.setDraftSkill", Act(a => _library.SetDraftSkill(Str(a, "value"))));
        router.Register("library.setDraftJson", Act(a => _library.SetDraftJson(Str(a, "value"))));
        router.Register("library.setPasteText", Act(a => _library.SetPasteText(Str(a, "value"))));
        router.Register("library.addToBasket", Act(a => _library.AddToBasket(Str(a, "title"))));
        router.Register("library.removeFromBasket", Act(a => _library.RemoveFromBasket(Str(a, "title"))));
        router.Register("library.clearBasket", Act(_ => _library.ClearBasket()));
        router.Register("library.startPaper", Act(a => _library.StartPaper(Str(a, "title"))));
        router.Register("library.editPaper", Act(a =>
        {
            // Opening in the editor is a cross service action: load the paper
            // there, then tell the shell to move to the Editor page.
            _editor.OpenPaper(Str(a, "title"));
            return _library.Snapshot("editor");
        }));
        router.Register("library.startBasket", Act(_ => _library.StartBasket()));
        router.Register("library.duplicatePaper", Act(a => _library.DuplicatePaper(Str(a, "title"))));
        router.Register("library.deletePaper", Act(a => _library.DeletePaper(Str(a, "title"))));
        router.Register("library.importContent", Act(a => _library.ImportContent(Str(a, "fileName"), Str(a, "content"))));
        router.Register("library.importBinary", Act(a => _library.ImportBinary(Str(a, "fileName"), Str(a, "base64"))));
        router.Register("library.buildDraftFromPaste", Act(_ => _library.BuildDraftFromPaste()));
        router.Register("library.aiFormatPaste", ActAsync(async (_, ct) =>
        {
            await _library.AiFormatPasteAsync(ct);
            return _library.Snapshot();
        }));
        router.Register("library.readImages", ActAsync(async (a, ct) =>
        {
            await _library.ReadImagesAsync(Images(a), ct);
            return _library.Snapshot();
        }));
        router.Register("library.aiCheckPaper", ActAsync(async (a, ct) =>
        {
            await _library.AiCheckPaperAsync(Str(a, "title"), ct);
            return _library.Snapshot();
        }));
        router.Register("library.cancelAi", Act(_ => _library.CancelAi()));
        router.Register("library.newManualTemplate", Act(_ => _library.NewManualTemplate()));
        router.Register("library.saveDraftJson", Act(_ => _library.SaveDraftJson()));
        router.Register("library.exportShown", Act(_ => _library.ExportShown()));
        router.Register("library.openExportFolder", Act(_ =>
        {
            // Open the same folder the last export wrote to; falls back to the
            // export root before any export has run.
            OpenFolder(_library.ExportFolder);
            return _library.Snapshot();
        }));

        // ---- Editor ----
        router.Register("editor.snapshot", Act(_ => _editor.Snapshot()));
        router.Register("editor.new", Act(_ => _editor.NewPaper()));
        router.Register("editor.setPaperField", Act(a =>
            _editor.SetPaperField(Str(a, "field"), Str(a, "value"))));
        router.Register("editor.setPartField", Act(a =>
            _editor.SetPartField(Int(a, "index"), Str(a, "field"), Str(a, "value"))));
        router.Register("editor.setQuestionField", Act(a =>
            _editor.SetQuestionField(Int(a, "partIndex"), Int(a, "questionIndex"), Str(a, "field"), Str(a, "value"))));
        router.Register("editor.selectPart", Act(a => _editor.SelectPart(Int(a, "index"))));
        router.Register("editor.selectQuestion", Act(a => _editor.SelectQuestion(Int(a, "index"))));
        router.Register("editor.addPart", Act(a => _editor.AddPart(Str(a, "skill"))));
        router.Register("editor.removePart", Act(a => _editor.RemovePart(Int(a, "index"))));
        router.Register("editor.addQuestion", Act(a => _editor.AddQuestion(Int(a, "partIndex"))));
        router.Register("editor.removeQuestion", Act(a =>
            _editor.RemoveQuestion(Int(a, "partIndex"), Int(a, "questionIndex"))));
        router.Register("editor.validate", Act(_ => _editor.Validate()));
        router.Register("editor.save", Act(_ => _editor.Save()));
        router.Register("editor.delete", Act(_ => _editor.Delete()));
        router.Register("editor.duplicate", Act(_ => _editor.Duplicate()));
        router.Register("editor.testPaper", Act(_ => _editor.TestPaper()));
        router.Register("editor.importContent", Act(a =>
            _editor.ImportContent(Str(a, "fileName"), Str(a, "content"))));
        router.Register("editor.importBinary", Act(a =>
            _editor.ImportBinary(Str(a, "fileName"), Str(a, "base64"))));
        router.Register("editor.buildPartFromPaste", Act(_ => _editor.BuildPartFromPaste()));
        router.Register("editor.aiDraft", ActAsync(async (_, ct) =>
        {
            await _editor.AiDraftAsync(ct);
            return _editor.Snapshot();
        }));
        router.Register("editor.readImages", ActAsync(async (a, ct) =>
        {
            await _editor.ReadImagesIntoPartAsync(Images(a), ct);
            return _editor.Snapshot();
        }));
        router.Register("editor.cancelAi", Act(_ => _editor.CancelAi()));

        // ---- Results ----
        router.Register("results.snapshot", Act(_ => _results.Snapshot()));
        router.Register("results.setScope", Act(a => _results.SetScopeFilter(Str(a, "value"))));
        router.Register("results.setSearch", Act(a => _results.SetSearch(Str(a, "value"))));
        router.Register("results.clearAll", Act(_ => _results.ClearAll()));

        // ---- Servers ----
        router.Register("servers.snapshot", Act(_ => _servers.Snapshot()));
        router.Register("servers.select", Act(a => _servers.SelectServer(Str(a, "url"))));
        router.Register("servers.connect", ActAsync(async (_, ct) =>
        {
            await _servers.ConnectAsync(ct);
            return _servers.Snapshot();
        }));
        router.Register("servers.add", Act(_ => _servers.AddServer()));
        router.Register("servers.remove", Act(_ => _servers.RemoveServer()));
        router.Register("servers.cancel", Act(_ => _servers.Cancel()));
        router.Register("servers.setNewName", Act(a => _servers.SetNewName(Str(a, "value"))));
        router.Register("servers.setNewUrl", Act(a => _servers.SetNewUrl(Str(a, "value"))));
        router.Register("servers.setAuthMode", Act(a => _servers.SetAuthMode(Str(a, "value"))));
        router.Register("servers.setUsername", Act(a => _servers.SetUsername(Str(a, "value"))));
        router.Register("servers.setSecret", Act(a => _servers.SetSecret(Str(a, "value"))));
        router.Register("servers.setAllowInsecure", Act(a => _servers.SetAllowInsecure(Bool(a, "value"))));
        router.Register("servers.setSearch", Act(a => _servers.SetSearch(Str(a, "value"))));
        router.Register("servers.setCategory", Act(a => _servers.SetCategory(Str(a, "value"))));
        router.Register("servers.downloadPaper", ActAsync(async (a, ct) =>
        {
            await _servers.DownloadPaperAsync(Str(a, "id"), ct);
            return _servers.Snapshot();
        }));
        router.Register("servers.downloadAll", ActAsync(async (_, ct) =>
        {
            await _servers.DownloadAllAsync(ct);
            return _servers.Snapshot();
        }));

        // ---- Settings ----
        router.Register("settings.snapshot", Act(_ => _settings.Snapshot()));

        // The offline model list with source and license, shown in Settings.
        router.Register("settings.models", () => OnnxModelRegistry.Slots
            .Select(slot => new
            {
                name = slot.Name,
                skill = slot.Skill,
                purpose = slot.Purpose,
                license = slot.License,
                source = slot.Source,
                fileName = slot.FileName,
                ready = OnnxModelRegistry.IsComplete(slot.Name),
            })
            .ToList());
        router.Register("settings.setBaseUrl", Act(a => _settings.SetBaseUrl(Str(a, "value"))));
        router.Register("settings.setModel", Act(a => _settings.SetModel(Str(a, "value"))));
        router.Register("settings.setApiKey", Act(a => _settings.SetApiKey(Str(a, "value"))));
        router.Register("settings.setTemperature", Act(a => _settings.SetTemperature(Dbl(a, "value"))));
        router.Register("settings.setMaxTokens", Act(a => _settings.SetMaxTokens(Int(a, "value"))));
        router.Register("settings.setTopP", Act(a => _settings.SetTopP(Dbl(a, "value"))));
        router.Register("settings.setTimeout", Act(a => _settings.SetTimeoutSeconds(Int(a, "value"))));
        router.Register("settings.setSystemPrompt", Act(a => _settings.SetSystemPrompt(Str(a, "value"))));
        router.Register("settings.setUseStreaming", Act(a => _settings.SetUseStreaming(Bool(a, "value"))));
        router.Register("settings.setVision", Act(a => _settings.SetVisionEnabled(Bool(a, "value"))));
        router.Register("settings.setModelAutoLoad", Act(a => _settings.SetModelAutoLoad(Bool(a, "value"))));
        router.Register("settings.setSpeakingAutoSubmit", Act(a => _settings.SetSpeakingAutoSubmit(Bool(a, "value"))));
        router.Register("settings.setUpdateCheckOnStartup", Act(a => _settings.SetUpdateCheckOnStartup(Bool(a, "value"))));
        router.Register("settings.setTextSize", Act(a => _settings.SetTextSize(Str(a, "value"))));
        router.Register("settings.setTheme", Act(a => _settings.SetTheme(Str(a, "value"))));
        router.Register("settings.setFullscreenOnStart", Act(a => _settings.SetFullscreenOnStart(Bool(a, "value"))));
        router.Register("settings.setAudioOutput", Act(a => _settings.SetAudioOutput(Str(a, "value"))));
        router.Register("settings.setAudioInput", Act(a => _settings.SetAudioInput(Str(a, "value"))));
        router.Register("settings.save", Act(_ => _settings.Save()));
        router.Register("settings.reset", Act(_ => _settings.Reset()));
        router.Register("settings.compactDatabase", Act(_ => _settings.CompactDatabase()));
        router.Register("settings.testConnection", ActAsync(async (_, ct) =>
        {
            await _settings.TestConnectionAsync(ct);
            return _settings.Snapshot();
        }));
        router.Register("settings.checkUpdate", ActAsync(async (_, ct) =>
        {
            await _settings.CheckForUpdatesAsync(ct);
            return _settings.Snapshot();
        }));
        router.Register("settings.downloadUpdate", ActAsync(async (_, ct) =>
        {
            await _settings.DownloadUpdateAsync(ct);
            return _settings.Snapshot();
        }));
        router.Register("settings.applyUpdate", Act(_ => _settings.ApplyUpdate()));
        router.Register("settings.openDataFolder", Act(_ =>
        {
            OpenFolder(_settings.Snapshot().DataFolder);
            return _settings.Snapshot();
        }));
        router.Register("settings.openProjectPage", Act(_ =>
        {
            OpenUrl(SettingsService.ProjectUrl);
            return _settings.Snapshot();
        }));
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception) { /* the UI still shows the path, so nothing is lost */ }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception) { /* the UI still shows the link, so nothing is lost */ }
    }

    private Func<JsonElement?, CancellationToken, Task<object?>> Act(Func<JsonElement?, object?> handler)
        => (args, _) => Task.FromResult(handler(args));

    private Func<JsonElement?, CancellationToken, Task<object?>> ActAsync(
        Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => handler;

    private static string Str(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;

    /// <summary>Reads a list of imported pictures, each a name and base64 payload.</summary>
    private static IReadOnlyList<ImportedImage> Images(JsonElement? a)
    {
        var list = new List<ImportedImage>();
        if (a is { } e && e.TryGetProperty("images", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "image" : "image";
                var base64 = item.TryGetProperty("base64", out var b) ? b.GetString() ?? string.Empty : string.Empty;
                var media = item.TryGetProperty("mediaType", out var m) ? m.GetString() ?? "image/png" : "image/png";
                if (base64.Length > 0) list.Add(new ImportedImage(name, base64, media));
            }
        }
        return list;
    }

    private static int Int(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool Bool(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            && v.GetBoolean();

    private static double Dbl(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
