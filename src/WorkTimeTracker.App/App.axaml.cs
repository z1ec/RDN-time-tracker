using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.App.Views;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _window;

    /// <summary>Параметры запуска, переданные из <see cref="Program"/>.</summary>
    internal static CommandLineOptions StartupOptions { get; set; } = CommandLineOptions.Parse([]);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = DataPaths.ResolveForCurrentProcess(StartupOptions.DataDir);
            AppLogging.Configure(paths.LogsDirectory);
            Log.Information("Запуск WorkTime Tracker {Version}, данные: {Dir} ({Kind})", AppInfo.Version, paths.DataDirectory, paths.Kind);
            InstallErrorHandlers(paths);

            _services = AppComposition.Build(paths, TimeProvider.System, new UserInteraction(() => _window));
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            _window = new MainWindow { DataContext = vm };
            WindowPlacementService.Restore(_window, _services.GetRequiredService<SettingsService>());
            _window.Opened += async (_, _) =>
            {
                if (!await vm.InitializeAsync())
                {
                    desktop.Shutdown(2);
                    return;
                }

                WindowPlacementService.Restore(_window, _services.GetRequiredService<SettingsService>());
            };
            _window.Closing += (_, _) => WindowPlacementService.Save(_window, _services.GetRequiredService<SettingsService>());
            desktop.MainWindow = _window;
            desktop.Exit += (_, _) =>
            {
                Log.Information("Завершение работы");
                Log.CloseAndFlush();
                _services?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Глобальная обработка ошибок (F7.10): всё пишется в лог, пользователь видит дружелюбный диалог.
    /// Данные не теряются: каждое изменение уже сохранено в БД в своей транзакции.
    /// </summary>
    private void InstallErrorHandlers(DataPaths paths)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Необработанное исключение (завершение: {Terminating})", e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Ненаблюдаемое исключение в задаче");
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "Необработанное исключение в UI");
            e.Handled = true;
            var interaction = _services?.GetService<IUserInteraction>();
            if (interaction is not null)
            {
                _ = interaction.ShowErrorAsync(
                    "Что-то пошло не так",
                    $"{e.Exception.Message}\n\nВсе сохранённые записи на месте. Подробности записаны в журнал программы — папка logs в папке данных.",
                    paths.LogsDirectory);
            }
        };
    }
}
