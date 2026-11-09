using Avalonia;
using WorkTimeTracker.App.Services;

namespace WorkTimeTracker.App;

/// <summary>
/// Точка входа. Разбирает аргументы командной строки и либо выполняет CLI-режим
/// (<c>--version</c>, <c>--self-test</c>), либо запускает UI.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        if (options.ShowVersion)
        {
            ConsoleAttacher.TryAttachToParent();
            Console.WriteLine($"WorkTime Tracker {AppInfo.Version}");
            return 0;
        }

        if (options.SelfTest)
        {
            ConsoleAttacher.TryAttachToParent();
            return SelfTest.Run(options, TimeProvider.System, Console.Out);
        }

        App.StartupOptions = options;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>Конфигурация Avalonia; используется также дизайнером.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
