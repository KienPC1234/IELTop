using System.Globalization;
using System.Text.Json;
using IELTop.Desktop.Web;
using IELTop.Services.App;
using IELTop.Services.Exam;
using IELTop.Services.Storage;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// Wires the web UI to the host. Everything the bridge can be asked to do is
/// registered from here, including the test window, so the composition root
/// only has to build the container and start this once.
/// </summary>
public sealed class HostBridgeSetup
{
    private readonly BridgeRouter _router;
    private readonly StaticFileServer _server;
    private readonly ExamEngine _engine;
    private readonly MauiExamSession _session;
    private readonly IStatsService _stats;
    private readonly ExamBridge _examBridge;
    private readonly AppBridge _appBridge;
    private readonly StudyBridge _studyBridge;
    private readonly DiagnosticsBridge _diagnosticsBridge;
    private readonly LibraryService _library;
    private readonly EditorService _editor;

    private Window? _examWindow;

    public HostBridgeSetup(
        BridgeRouter router,
        StaticFileServer server,
        ExamEngine engine,
        MauiExamSession session,
        IStatsService stats,
        ExamBridge examBridge,
        AppBridge appBridge,
        StudyBridge studyBridge,
        DiagnosticsBridge diagnosticsBridge,
        LibraryService library,
        EditorService editor)
    {
        _router = router;
        _server = server;
        _engine = engine;
        _session = session;
        _stats = stats;
        _examBridge = examBridge;
        _appBridge = appBridge;
        _studyBridge = studyBridge;
        _diagnosticsBridge = diagnosticsBridge;
        _library = library;
        _editor = editor;
    }

    /// <summary>
    /// Registers every action the UI can call. Runs once, before the first
    /// window opens, so a page never calls into an action that does not exist.
    /// </summary>
    public void Register()
    {
        _server.Router = _router;

        _examBridge.Register(_router);
        _appBridge.Register(_router);
        _studyBridge.Register(_router);
        _diagnosticsBridge.Register(_router);

        // A draft the AI finished belongs in the editor, so the two screens
        // agree without the page having to ask twice.
        _library.DraftReady += paper => _editor.LoadDraft(paper);

        RegisterDashboard();
        RegisterWindowRoutes();

        // The test window is held back only while a test is on the clock. When
        // nothing is at risk the close goes through, so a finished test never
        // asks a question whose answer cannot be lost anyway.
        _session.CloseGuard = () => _engine.Run.Phase is ExamPhase.PartIntro or ExamPhase.Running;
        _session.CloseRequested += () => _router.Broadcast("exam.closeRequested", null);
    }

    private void RegisterWindowRoutes()
    {
        _router.Register("window.toggleFullscreen", () =>
        {
            _session.ToggleFullscreen();
            return null;
        });

        _router.Register("window.setFullscreen", (JsonElement? args, CancellationToken _) =>
        {
            bool on = args is { } a && a.TryGetProperty("value", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                && v.GetBoolean();

            if (on) _session.EnterFullscreen();
            else _session.ExitFullscreen();

            return Task.FromResult<object?>(null);
        });

        _router.Register("exam.openWindow", () =>
        {
            MainThread.BeginInvokeOnMainThread(OpenExamWindow);
            return null;
        });

        _router.Register("exam.closeWindow", () =>
        {
            if (_engine.Run.Phase is ExamPhase.PartIntro or ExamPhase.Running)
            {
                return new { needsConfirm = true };
            }

            CloseExamWindow();
            return null;
        });

        _router.Register("exam.confirmCloseWindow", () =>
        {
            CloseExamWindow();
            return null;
        });
    }

    private void OpenExamWindow()
    {
        if (_examWindow is not null)
        {
            // The window is already up: just bring it back and fill the screen.
            _session.Focus();
            _session.EnterFullscreen();
            return;
        }

        var examPage = new ExamPage(_server, _router, _session);
        _examWindow = new Window(examPage)
        {
            Title = "IELTop - Test in progress",
            Width = 1360,
            Height = 900,
            MinimumWidth = 1100,
            MinimumHeight = 700,
        };

        _session.Attach(_examWindow);

        _examWindow.Destroying += (_, _) =>
        {
            _engine.CancelRunningTest();
            _session.Attach(null);
            _examWindow = null;
        };

        Application.Current?.OpenWindow(_examWindow);
        _session.EnterFullscreen();
    }

    /// <summary>
    /// Closes the test window for good. The close is approved first, otherwise
    /// the title bar guard would hold this very close back.
    /// </summary>
    private void CloseExamWindow()
    {
        _session.ApproveClose();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_examWindow is not null)
            {
                Application.Current?.CloseWindow(_examWindow);
                _examWindow = null;
            }
        });
    }

    private void RegisterDashboard()
    {
        _router.Register("dashboard.get", () =>
        {
            var s = _stats.Build();
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

    private static string AppVersion()
    {
        var version = typeof(HostBridgeSetup).Assembly.GetName().Version;
        return version is null ? "1.0.0" : version.ToString(3);
    }
}