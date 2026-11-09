using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App.Tests;

/// <summary>
/// Генерация скриншотов для README и визуальной самопроверки (§8.3). Настоящий Skia, шрифт Inter,
/// демо-данные во временной БД, «сегодня» — четверг 12.11.2026 16:20.
/// В обычном прогоне исключены (Category=Screenshots), запуск — scripts/screenshots.sh.
/// </summary>
[Trait("Category", "Screenshots")]
public class ScreenshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 16, 20, 0, TimeSpan.Zero);

    private static string OutputDir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("SCREENSHOTS_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName, "WorkTimeTracker.slnx")))
                {
                    root = root.Parent;
                }

                dir = Path.Combine(root?.FullName ?? ".", "docs", "images");
            }

            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // Короткий «человеческий» путь к данным для снимков настроек и «О программе».
    private static string DemoDataDir => OperatingSystem.IsWindows()
        ? Path.Combine(Path.GetTempPath(), "WorkTimeTracker")
        : "/tmp/WorkTimeTracker";

    private static async Task<(TestHost Host, MainWindow Window)> OpenAsync(ThemeVariant theme, double height = 780)
    {
        var host = await TestHost.StartAsync(Now, demoData: true, dataDir: DemoDataDir);
        Application.Current!.RequestedThemeVariant = theme; // после инициализации: она применяет тему из настроек
        var window = await UiTestHelpers.ShowMainWindowAsync(host, height: height);
        return (host, window);
    }

    private static async Task SaveAsync(MainWindow window, string name)
    {
        await UiTestHelpers.SettleAsync(10);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(OutputDir, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Journal_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            host.Journal.QuickInput = "1630 1800 ревью pull request";
            await SaveAsync(window, "journal-light.png");
        }
    }

    [AvaloniaFact]
    public async Task Journal_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark);
        using (host)
        {
            host.Journal.IsFormExpanded = true;
            host.Journal.FormStart = new TimeSpan(22, 0, 0);
            host.Journal.FormEnd = new TimeSpan(1, 30, 0);
            host.Journal.FormComment = "ночная выкатка";
            await SaveAsync(window, "journal-dark.png");
        }
    }

    [AvaloniaFact]
    public async Task QuickInput_Error()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            host.Journal.QuickInput = "0900 2500 созвон";
            await SaveAsync(window, "quick-input-error.png");
        }
    }

    [AvaloniaFact]
    public async Task Statistics_Month_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            host.Main.NavigateCommand.Execute("2");
            host.Statistics.SelectedKindIndex = 2;
            await UiTestHelpers.WaitForAsync(() => host.Statistics.Period.Kind == Core.Model.PeriodKind.Month && host.Statistics.HasChart);
            await SaveAsync(window, "statistics-month-light.png");
        }
    }

    [AvaloniaFact]
    public async Task Statistics_Week_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark);
        using (host)
        {
            host.Main.NavigateCommand.Execute("2");
            await UiTestHelpers.WaitForAsync(() => host.Statistics.HasChart);
            await SaveAsync(window, "statistics-week-dark.png");
        }
    }

    [AvaloniaFact]
    public async Task Statistics_Year_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light, height: 1240);
        using (host)
        {
            host.Main.NavigateCommand.Execute("2");
            host.Statistics.SelectedKindIndex = 3;
            await UiTestHelpers.WaitForAsync(() => host.Statistics.HasHeatmap);
            await SaveAsync(window, "statistics-year-light.png");
        }
    }

    [AvaloniaFact]
    public async Task Settings_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            host.Main.NavigateCommand.Execute("3");
            await SaveAsync(window, "settings-light.png");
        }
    }

    [AvaloniaFact]
    public async Task About_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark);
        using (host)
        {
            host.Main.NavigateCommand.Execute("4");
            await SaveAsync(window, "about-dark.png");
        }
    }
}
