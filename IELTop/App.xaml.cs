using System.Windows;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using IELTop.Services.Storage;
using IELTop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IELTop
{
    public partial class App : Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ISettingsStore, SettingsStore>();
                    services.AddSingleton<IStatsService, StatsService>();
                    services.AddSingleton<IFileTextExtractor, FileTextExtractor>();
                    services.AddSingleton<IFileDialogService, FileDialogService>();
                    services.AddSingleton<IContentImportService, ContentImportService>();
                    services.AddSingleton<IPaperExporter, PaperExporter>();
                    services.AddSingleton<IOnnxService, OnnxService>();
                    services.AddSingleton<IAudioService, SimpleAudioService>();
                    services.AddSingleton<IG2PService, SimpleG2PService>();
                    services.AddSingleton<IMddPhonemeService, MddPhonemeService>();
                    services.AddSingleton<IExamRepository, ExamRepository>();
                    services.AddSingleton<IPracticeRepository, PracticeRepository>();
                    services.AddSingleton<ILlmService, OpenAiCompatibleLlmService>();
                    services.AddSingleton<IIeltsAiService, IeltsAiService>();

                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<SpeakingViewModel>();
                    services.AddSingleton<ExamViewModel>();
                    services.AddSingleton<VocabularyViewModel>();
                    services.AddSingleton<WritingViewModel>();
                    services.AddSingleton<ReadingViewModel>();
                    services.AddSingleton<ListeningViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<ContentViewModel>();

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
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
        }
    }
}
