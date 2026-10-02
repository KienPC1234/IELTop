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

namespace IELTop.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var server = new StaticFileServer(webRoot);
        server.AddMediaFolder("audio", Path.Combine(AppContext.BaseDirectory, "Assets", "Audio"));
        server.AddMediaFolder("audio", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Audio"));
        server.AddMediaFolder("images", Path.Combine(AppContext.BaseDirectory, "Assets", "Images"));
        server.AddMediaFolder("images", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Images"));
        server.Start();

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

        var examSession = new MauiExamSession();
        var engine = new ExamEngine(repository, ai, stt, gec, models, pronunciation, examSession);

        var updates = new GithubReleaseUpdateService();
        var settingsService = new SettingsService(settings, ai, updates);
        var resultsService = new ResultsService();
        var libraryService = new LibraryService(repository, engine, ai);
        var editorService = new EditorService(repository, engine, ai);
        libraryService.DraftReady += paper => editorService.LoadDraft(paper);

        var serverStore = new ContentServerStore();
        var serverClient = new ContentServerClient();
        var serversService = new ServersService(serverStore, serverClient, repository, engine);

        var router = new BridgeRouter();
        RegisterDashboard(router, stats);

        var examBridge = new ExamBridge(engine, server, examSession.Push);
        examBridge.Register(router);
        new AppBridge(libraryService, editorService, resultsService, serversService, settingsService).Register(router);

        Window? examWindow = null;
        var isExamFullscreen = false;

        router.Register("window.toggleFullscreen", () =>
        {
            examSession.ToggleFullscreen(ref isExamFullscreen);
            return null;
        });

        router.Register("window.setFullscreen", (JsonElement? args, CancellationToken _) =>
        {
            bool on = args is { } a && a.TryGetProperty("value", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                && v.GetBoolean();
            isExamFullscreen = on;
            if (on) examSession.EnterFullscreen();
            else examSession.ExitFullscreen();
            return Task.FromResult<object?>(null);
        });

        router.Register("exam.openWindow", () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (examWindow is not null)
                {
                    examSession.Focus();
                    return;
                }

                var examPage = new ExamPage(server, router, engine, examSession);
                examWindow = new Window(examPage)
                {
                    Title = "IELTop - Test in progress",
                    Width = 1360,
                    Height = 900,
                    MinimumWidth = 1100,
                    MinimumHeight = 700,
                };

                examSession.Attach(examWindow);

                examWindow.Destroying += (_, _) =>
                {
                    engine.CancelRunningTest();
                    examSession.Attach(null);
                    examWindow = null;
                };

                Application.Current?.OpenWindow(examWindow);
            });
            return null;
        });

        router.Register("exam.closeWindow", () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (examWindow is not null)
                {
                    Application.Current?.CloseWindow(examWindow);
                    examWindow = null;
                }
            });
            return null;
        });

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(_ => { });

        builder.Services.AddSingleton(server);
        builder.Services.AddSingleton(router);
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(examSession);
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }

    private static string AppVersion()
    {
        var version = typeof(MauiProgram).Assembly.GetName().Version;
        return version is null ? "1.0.0" : version.ToString(3);
    }

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
