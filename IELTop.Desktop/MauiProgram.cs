using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Desktop.Bridge;
using IELTop.Desktop.Diagnostics;
using IELTop.Desktop.Update;
using IELTop.Desktop.Web;
using IELTop.Services.Ai;
using IELTop.Services.App;
using IELTop.Services.Audio;
using IELTop.Services.Diagnostics;
using IELTop.Services.Exam;
using IELTop.Services.Learn;
using IELTop.Services.Protocol;
using IELTop.Services.Storage;
using IELTop.Services.Update;
namespace IELTop.Desktop;

/// <summary>
/// The composition root. Every service is built here by the container and
/// receives its dependencies through the constructor, so nothing reaches for a
/// shared instance by hand and the container can dispose what owns a resource.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // The log is opened first, before any service, so a failure while the
        // services are built is still written down.
        AppLog.Initialize("IELTop.Desktop", AppVersion());
        AppLog.Info("app", "Host starting.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            HealthMonitor.CountUnhandled();
            AppLog.Error("app", "Unhandled domain exception.", e.ExceptionObject as Exception);
            AppLog.Flush();
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            HealthMonitor.CountUnhandled();
            AppLog.Error("app", "Unobserved task exception.", e.Exception);
            AppLog.Flush();
        };

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(_ => { });

        // BlazorWebView resolves its services from the container. Without this
        // registration the view throws while it initializes, which surfaces as a
        // native WinUI crash rather than a managed error.
        builder.Services.AddMauiBlazorWebView();

        RegisterDesktopServices(builder.Services);
        RegisterCoreServices(builder.Services);

        // The database has to exist before any service reads it.
        AppDbContext.EnsureCreated();

        var app = builder.Build();

        // Routes are wired before the first window opens, so a page never asks
        // for an action the host has not registered yet.
        app.Services.GetRequiredService<HostBridgeSetup>().Register();

        // Run the headless self test and exit when asked, so the app can be
        // checked without opening any window. It runs on a pool thread: the UI
        // thread has a synchronization context, so waiting on the live model
        // calls here would deadlock.
        if (SelfTestRequested())
        {
            var runner = app.Services.GetRequiredService<SelfTestRunner>();
            var ok = Task.Run(runner.Run).GetAwaiter().GetResult();
            Environment.Exit(ok ? 0 : 2);
        }

        // Serve the offline ONNX models over loopback HTTP and keep running, so
        // another program can use them without loading the native runtime itself.
        if (ModelServerRequested())
        {
            var server = app.Services.GetRequiredService<OnnxHttpServer>();
            server.Start();
            AppLog.Info("app", "Model server mode: serving ONNX over HTTP. Press Ctrl+C to stop.");
            var stop = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
            stop.Wait();
            server.Dispose();
            Environment.Exit(0);
        }

        AppLog.Info("app", "Host ready.");
        return app;
    }

    /// <summary>True when the app was started with --selftest.</summary>
    private static bool SelfTestRequested() =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the app was started with --model-server.</summary>
    private static bool ModelServerRequested() =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--model-server", StringComparison.OrdinalIgnoreCase));

    private static string AppVersion()
    {
        try
        {
            return typeof(MauiProgram).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    private static void RegisterDesktopServices(IServiceCollection services)
    {
        // The local server that hands the UI to the WebView and carries the
        // bridge calls. Registered as a singleton, so the container stops the
        // listener when the app shuts down.
        services.AddSingleton<StaticFileServer>(_ =>
        {
            var server = new StaticFileServer(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
            var shipped = AppContext.BaseDirectory;
            var userContent = Path.Combine(UserContentRoot);

            server.AddMediaFolder("audio", Path.Combine(shipped, "Assets", "Audio"));
            server.AddMediaFolder("audio", Path.Combine(userContent, "Audio"));
            server.AddMediaFolder("images", Path.Combine(shipped, "Assets", "Images"));
            server.AddMediaFolder("images", Path.Combine(userContent, "Images"));
            server.Start();
            return server;
        });

        services.AddSingleton<BridgeRouter>();

        // The Blazor side of the bridge. The page calls into this over JS
        // interop; the HTTP channel stays as the fallback for the plain WebView2
        // host, which has no Blazor runtime.
        services.AddSingleton<JsBridge>();

        // The exam window controller is both the bridge target and the port the
        // engine talks to, so it must be the same object in both places.
        services.AddSingleton<MauiExamSession>();
        services.AddSingleton<IExamSessionController>(sp => sp.GetRequiredService<MauiExamSession>());

        services.AddSingleton<HostBridgeSetup>();
        services.AddSingleton<AppBridge>();
        services.AddSingleton<StudyBridge>();
        services.AddSingleton<DiagnosticsBridge>();
        services.AddSingleton<Diagnostics.SelfTestRunner>();

        // Engine events reach the pages over the HTTP event stream only. Sending
        // them down the window message channel as well made every page handle
        // each event twice, because the page listens on both channels.
        services.AddSingleton(sp => new ExamBridge(
            sp.GetRequiredService<ExamEngine>(),
            sp.GetRequiredService<StaticFileServer>(),
            message => sp.GetRequiredService<BridgeRouter>().Broadcast("exam.push", message)));

        services.AddSingleton<MainPage>();
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsStore>());

        // The ONNX sessions and the community server client hold native and
        // network resources, so they are registered as the concrete type and
        // mapped to the interface: the container then disposes them once.
        // The execution provider is picked here: a GPU (DirectML) when the PC has
        // one, CPU otherwise. The detection lives in the host, so Core stays OS
        // neutral and works on a machine with no GPU.
        services.AddSingleton<DirectMlExecutionProvider>();
        services.AddSingleton<IOnnxExecutionProvider>(sp => sp.GetRequiredService<DirectMlExecutionProvider>());
        services.AddSingleton<OnnxService>(sp =>
            new OnnxService(sp.GetRequiredService<IOnnxExecutionProvider>()));
        services.AddSingleton<IOnnxService>(sp => sp.GetRequiredService<OnnxService>());

        services.AddSingleton<ContentServerClient>();
        services.AddSingleton<IContentServerClient>(sp => sp.GetRequiredService<ContentServerClient>());

        services.AddSingleton<IContentServerStore, ContentServerStore>();
        services.AddSingleton<IExamRepository, ExamRepository>();
        services.AddSingleton<IUpdateService, GithubReleaseUpdateService>();

        services.AddSingleton<ILlmService, OpenAiCompatibleLlmService>();
        services.AddSingleton<IIeltsAiService, IeltsAiService>();
        services.AddSingleton<ISttService, SttService>();
        services.AddSingleton<IGecService, GecService>();
        services.AddSingleton<IModelLoadCoordinator, ModelLoadCoordinator>();
        services.AddSingleton<IG2PService, SimpleG2PService>();
        services.AddSingleton<IMddPhonemeService, MddPhonemeService>();
        // The neural voice, with no OS fallback: the HTTP server must be able to
        // report whether the real Piper files are installed, not a system voice.
        services.AddSingleton<ITtsService>(sp => new PiperTtsService(sp.GetRequiredService<IOnnxService>()));
        services.AddSingleton(sp => new OnnxHttpServer(
            sp.GetRequiredService<IOnnxService>(),
            sp.GetRequiredService<ISttService>(),
            sp.GetRequiredService<ITtsService>(),
            sp.GetRequiredService<IMddPhonemeService>(),
            sp.GetRequiredService<IGecService>()));
        services.AddSingleton<IPronunciationService, PronunciationService>();

        services.AddSingleton<IStatsService, StatsService>();
        services.AddSingleton<ExamEngine>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ResultsService>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<EditorService>();
        services.AddSingleton<ServersService>();
        services.AddSingleton<LessonService>();
        services.AddSingleton<StudyService>();
        services.AddSingleton<SpeakingTutorService>();
        services.AddSingleton(sp => new DiagnosticsService(AppVersion()));
    }

    /// <summary>Where content the student brings lives, never inside the install folder.</summary>
    private static string UserContentRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content");
}