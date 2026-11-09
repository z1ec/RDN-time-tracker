using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App;

public partial class App : Application
{
    /// <summary>Параметры запуска, переданные из <see cref="Program"/>.</summary>
    internal static CommandLineOptions StartupOptions { get; set; } = CommandLineOptions.Parse([]);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
