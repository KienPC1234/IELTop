using System.Drawing;
using System.Globalization;
using System.Text.Json;
using IELTop.Data;
using IELTop.Desktop.Bridge;
using IELTop.Desktop.Update;
using IELTop.Desktop.Web;
using IELTop.Services.Ai;
using IELTop.Services.App;
using IELTop.Services.Exam;
using IELTop.Services.Storage;
using IELTop.Services.Update;
using Photino.NET;

namespace IELTop.Desktop;

/// <summary>
/// The cross platform desktop client. A main native window hosts the app UI,
/// and a test runs in its own native window so a full screen exam never has to
/// share the shell. Both windows talk to the same Core services through a
/// small JSON bridge. No ASP.NET, no Node at run time, nothing online.
///
/// ONNX inference runs here, in this process, on the same model files. The web
/// UI only draws; it never runs a model. Audio capture and playback belong to
/// the exam WebView, so no Windows only audio library is needed.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Band labels and every number the UI shows must read in English on
        // any machine, whatever the user's locale.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // The web UI is served from the loopback address so ES modules load.
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        using var server = new StaticFileServer(webRoot);
        server.AddMediaFolder("audio", Path.Combine(AppContext.BaseDirectory, "Assets", "Audio"));
        server.AddMediaFolder("audio", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Audio"));
        server.AddMediaFolder("images", Path.Combine(AppContext.BaseDirectory, "Assets", "Images"));
        server.AddMediaFolder("images", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Images"));
        server.Start();
        Console.WriteLine($"IELTop desktop UI at {server.BaseUrl}/index.html");

        // The same local database and settings the Windows app uses.
        AppDbContext.EnsureCreated();

        var settings = new SettingsStore();
        var onnx = new OnnxService();
        var stats = new StatsService(settings);
        var repository = new ExamRepository();
        var ai = new IeltsAiService(new OpenAiCompatibleLlmService(settings), settings);
        var stt = new SttService(onnx);
        var gec = new GecService(onnx);
        var models = new ModelLoadCoordinator(onnx, settings);
        var g2p = new SimpleG2PService();
        var pronunciation = new PronunciationService(new MddPhonemeService(onnx, g2p));
        using var engine = new ExamEngine(repository, ai, stt, gec, models, pronunciation);

        // Page services, all sharing the same Core the Windows app uses.
        var updates = new GithubReleaseUpdateService();
        var settingsService = new SettingsService(settings, ai, updates);
        var resultsService = new ResultsService();
        var libraryService = new LibraryService(repository, engine, ai);
        var editorService = new EditorService(repository, engine, ai);
        // A paper drafted from the Library opens straight in the Editor.
        libraryService.DraftReady += paper => editorService.LoadDraft(paper);
        var serverStore = new ContentServerStore();
        var serverClient = new ContentServerClient();
        var serversService = new ServersService(serverStore, serverClient, repository, engine);

        // The windows exist after the router is built, so the push reads them
        // through these boxes on every event.
        var shellBox = new WindowBox();
        var examBox = new WindowBox();
        var isExamFullscreen = false;

        // A single push sink: exam events go to the exam window.
        void Push(string message) => examBox.Current?.SendWebMessage(message);

        var router = new BridgeRouter();
        RegisterDashboard(router, stats);

        var examBridge = new ExamBridge(engine, server, Push);
        examBridge.Register(router);
        new AppBridge(libraryService, editorService, resultsService, serversService, settingsService)
            .Register(router);

        // The shell window: the whole app except a running test. Built first,
        // because the exam window is its child and shares its message loop.
        var shell = new PhotinoWindow()
            .SetTitle("IELTop")
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "app.ico"))
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1180, 820))
            .SetMinSize(980, 640)
            .SetLogVerbosity(Environment.GetEnvironmentVariable("IELTOP_LOG") == "1" ? 1 : 0)
#if DEBUG
            .SetDevToolsEnabled(true)
#endif
            .Center()
            .RegisterWebMessageReceivedHandler((sender, message) =>
            {
                if (sender is not PhotinoWindow origin) return;
                _ = Task.Run(async () =>
                {
                    var reply = await router.HandleAsync(message, CancellationToken.None);
                    if (!string.IsNullOrEmpty(reply))
                        origin.SendWebMessage(reply);
                });
            })
            .Load($"{server.BaseUrl}/index.html");

        shellBox.Current = shell;

        // The host owns the windows, so window controls and opening the exam
        // window live here, marshalled to the shell's own thread.
        router.Register("window.toggleFullscreen", () =>
        {
            shell.Invoke(() => FlipExamFullscreen(examBox, ref isExamFullscreen));
            return null;
        });
        router.Register("window.setFullscreen", (JsonElement? args, CancellationToken _) =>
        {
            bool on = args is { } a && a.TryGetProperty("value", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                && v.GetBoolean();
            shell.Invoke(() =>
            {
                isExamFullscreen = on;
                examBox.Current?.SetFullScreen(on);
            });
            return Task.FromResult<object?>(null);
        });

        // Opening and closing the exam window, called from the shell. The exam
        // page fetches exam.snapshot on load, so no push is needed here. A new
        // window must be built on the shell's own thread, so these marshal.
        router.Register("exam.openWindow", () =>
        {
            shell.Invoke(() => OpenExamWindow(shell, server.BaseUrl, examBox, isExamFullscreen, engine, router));
            return null;
        });
        router.Register("exam.closeWindow", () =>
        {
            shell.Invoke(() => examBox.Current?.Close());
            return null;
        });

        shell.WaitForClose();
    }

    /// <summary>Opens the dedicated exam window once; a later call focuses it.</summary>
    private static void OpenExamWindow(
        PhotinoWindow parent, string baseUrl, WindowBox examBox, bool fullscreen,
        ExamEngine engine, BridgeRouter router)
    {
        if (examBox.Current is { } existing)
        {
            // The window already exists; make sure it shows the current state.
            existing.SetFullScreen(fullscreen);
            return;
        }

        WindowBox box = examBox;
        // A child window must be built from the parent, so it shares the
        // parent's message loop. A second top level window would never run.
        var exam = new PhotinoWindow(parent)
            .SetTitle("IELTop, test in progress")
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "app.ico"))
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1360, 900))
            .SetMinSize(1100, 700)
            .SetLogVerbosity(Environment.GetEnvironmentVariable("IELTOP_LOG") == "1" ? 1 : 0)
#if DEBUG
            .SetDevToolsEnabled(true)
#endif
            .SetMediaAutoplayEnabled(true)
            // The exam records speech and plays clips, so the WebView needs
            // microphone and media permissions without a prompt.
            .SetGrantBrowserPermissions(true)
            .Center()
            .RegisterFocusOutHandler((_, _) => engine.RegisterFocusLost())
            .RegisterWebMessageReceivedHandler((sender, message) =>
            {
                if (sender is not PhotinoWindow origin) return;
                _ = Task.Run(async () =>
                {
                    var reply = await router.HandleAsync(message, CancellationToken.None);
                    if (!string.IsNullOrEmpty(reply))
                        origin.SendWebMessage(reply);
                });
            })
            .RegisterWindowClosingHandler((_, _) =>
            {
                // Leaving the exam window stops the run; answers are dropped.
                engine.CancelRunningTest();
                box.Current = null;
                return true;
            })
            .Load($"{baseUrl}/exam.html");

        box.Current = exam;
        // A child window runs its own message loop, so it must wait here, like
        // the Photino multi window sample. Without this it is created but never
        // shown.
        exam.WaitForClose();
    }

    private static void FlipExamFullscreen(WindowBox examBox, ref bool isFullscreen)
    {
        isFullscreen = !isFullscreen;
        examBox.Current?.SetFullScreen(isFullscreen);
    }

    /// <summary>Holds a window so the engine push can reach it after construction.</summary>
    private sealed class WindowBox
    {
        public PhotinoWindow? Current { get; set; }
    }

    private static string AppVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        return version is null ? "1.0.0" : version.ToString(3);
    }

    /// <summary>Registers dashboard methods on the router.</summary>
    private static void RegisterDashboard(BridgeRouter router, IStatsService stats)
    {
        router.Register("dashboard.get", () =>
        {
            var s = stats.Build();
            return new
            {
                version = AppVersion(),
                examAttempts = s.ExamAttempts,
                lastBandLabel = s.LastBandLabel,
                llmConfigured = s.LlmConfigured,
                llmModel = s.LlmModel,
                llmSummary = s.LlmConfigured ? s.LlmModel : "Not set",
                modelsReady = s.ModelsReady,
                modelsTotal = s.ModelsTotal,
                streakDays = s.StreakDays,
                activeToday = s.ActiveToday,
                streakLabel = s.StreakDays switch
                {
                    0 => "No streak yet",
                    1 => "1 day streak",
                    _ => $"{s.StreakDays} day streak",
                },
                streakHint = s.StreakDays == 0
                    ? "Finish a test or a speaking practice to start a streak."
                    : s.ActiveToday
                        ? "You studied today. Keep it going."
                        : "Study today to keep your streak alive.",
                weeklyActivity = s.WeeklyActivity.Select(d => new
                {
                    label = d.Date.ToString("ddd", CultureInfo.InvariantCulture),
                    count = d.Count,
                }),
                bandTrend = s.BandTrend.Select(b => new
                {
                    label = b.Label,
                    low = b.BandLow,
                    high = b.BandHigh,
                }),
                criteriaNote = "Bands are practice estimates only, never official IELTS scores.",
            };
        });
    }
}
