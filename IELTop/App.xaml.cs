using System.Windows;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using IELTop.Services.Storage;
using IELTop.Services.Update;
using IELTop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Velopack;

namespace IELTop
{
    public partial class App : Application
    {
        private IHost? _host;

        /// <summary>
        /// Custom entry point so Velopack can bootstrap before any WPF work.
        /// Velopack applies updates and restarts here, without loading the UI.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build().Run();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ISettingsStore, SettingsStore>();
                    services.AddSingleton<IStatsService, StatsService>();
                    services.AddSingleton<IOnnxService, OnnxService>();
                    services.AddSingleton<IAudioService, SimpleAudioService>();
                    services.AddSingleton<WindowsTtsService>();
                    services.AddSingleton<ITtsService, PiperTtsService>();
                    services.AddSingleton<ISttService, SttService>();
                    services.AddSingleton<IGecService, GecService>();
                    services.AddSingleton<IG2PService, SimpleG2PService>();
                    services.AddSingleton<IMddPhonemeService, MddPhonemeService>();
                    services.AddSingleton<IExamRepository, ExamRepository>();
                    services.AddSingleton<IModelLoadCoordinator, ModelLoadCoordinator>();
                    services.AddSingleton<IContentServerStore, ContentServerStore>();
                    services.AddSingleton<IContentServerClient, ContentServerClient>();
                    services.AddSingleton<ILlmService, OpenAiCompatibleLlmService>();
                    services.AddSingleton<IIeltsAiService, IeltsAiService>();
                    services.AddSingleton<IUpdateService, UpdateService>();

                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<ExamViewModel>();
                    services.AddSingleton<LibraryViewModel>();
                    services.AddSingleton<EditorViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<ResultsViewModel>();
                    services.AddSingleton<ServersViewModel>();

                    services.AddTransient<MainWindow>();
                })
                .Build();

            AppDbContext.EnsureCreated();

            var main = _host.Services.GetRequiredService<MainWindow>();
            main.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            main.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
            {
                (_host.Services.GetService<IOnnxService>() as IDisposable)?.Dispose();
                (_host.Services.GetService<IAudioService>() as IDisposable)?.Dispose();
                (_host.Services.GetService<IContentServerClient>() as IDisposable)?.Dispose();
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
        }
    }
}
