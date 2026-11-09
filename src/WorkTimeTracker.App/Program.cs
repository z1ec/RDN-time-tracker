using System.Globalization;
using Avalonia;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Formatting;

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
        // Даты, пикеры и числа — по-русски независимо от локали ОС (F7.7).
        CultureInfo.DefaultThreadCurrentCulture = RuFormat.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = RuFormat.Culture;
        CultureInfo.CurrentCulture = RuFormat.Culture;
        CultureInfo.CurrentUICulture = RuFormat.Culture;

        var options = CommandLineOptions.Parse(args);
        if (options.Errors.Count > 0)
        {
            ConsoleAttacher.TryAttachToParent();
            foreach (var error in options.Errors)
            {
                Console.Error.WriteLine(error);
            }

            return 1;
        }

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
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Конфигурация Avalonia; используется также дизайнером.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
