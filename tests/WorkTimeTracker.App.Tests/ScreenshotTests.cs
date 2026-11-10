using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
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

    private static async Task<(TestHost Host, MainWindow Window)> OpenAsync(ThemeVariant theme, double height = 780, bool notepad = false)
    {
        var host = notepad ? await StartWithNotepadAsync() : await TestHost.StartAsync(Now, demoData: true, dataDir: DemoDataDir);
        Application.Current!.RequestedThemeVariant = theme; // после инициализации: она применяет тему из настроек
        var window = await UiTestHelpers.ShowMainWindowAsync(host, height: height);
        return (host, window);
    }

    /// <summary>
    /// Демо-блокнот: два листа в архиве и текущий «Лендинг для студии». Листы ведутся «в прошлом»
    /// (часы хоста переводятся вперёд), затем поверх добавляются обычные демо-записи журнала.
    /// </summary>
    private static async Task<TestHost> StartWithNotepadAsync()
    {
        var host = new TestHost(new DateTimeOffset(2026, 10, 31, 19, 5, 0, TimeSpan.Zero), DemoDataDir);
        await host.Services.GetRequiredService<Data.DatabaseInitializer>().InitializeAsync();
        var notepad = host.NotepadService;

        async Task AddAsync(DateTimeOffset at, string text)
        {
            host.Time.SetUtcNow(at);
            var result = await notepad.AddFromTextAsync(text, host.Today);
            Assert.True(result.IsSuccess, string.Join("; ", result.Errors));
        }

        async Task ClearAsync(DateTimeOffset at)
        {
            host.Time.SetUtcNow(at);
            Assert.NotNull(await notepad.ClearAsync());
        }

        await AddAsync(new(2026, 10, 31, 19, 5, 0, TimeSpan.Zero), "1500 1900 разбор старого проекта");
        await AddAsync(new(2026, 11, 1, 18, 32, 0, TimeSpan.Zero), "1500 1830 оценка и план работ");
        await ClearAsync(new(2026, 11, 1, 18, 45, 0, TimeSpan.Zero));

        host.Time.SetUtcNow(new(2026, 11, 7, 14, 28, 0, TimeSpan.Zero));
        await notepad.SetTitleAsync("Заказ: сайт кафе");
        await AddAsync(new(2026, 11, 7, 19, 2, 0, TimeSpan.Zero), "1430 1900 макет главной страницы");
        await AddAsync(new(2026, 11, 8, 19, 20, 0, TimeSpan.Zero), "1500 1915 вёрстка меню и галереи");
        await AddAsync(new(2026, 11, 9, 8, 16, 0, TimeSpan.Zero), "0630 0815 правки по отзывам заказчика");
        await ClearAsync(new(2026, 11, 9, 8, 20, 0, TimeSpan.Zero));

        host.Time.SetUtcNow(new(2026, 11, 11, 8, 2, 0, TimeSpan.Zero));
        await notepad.SetTitleAsync("Лендинг для студии");
        await AddAsync(new(2026, 11, 11, 8, 2, 0, TimeSpan.Zero), "0630 0800 бриф и референсы");
        await AddAsync(new(2026, 11, 12, 8, 17, 0, TimeSpan.Zero), "0645 0815 структура страницы");

        host.Time.SetUtcNow(Now);
        await DemoData.SeedAsync(host.Entries, host.Today);
        Assert.True(await host.Main.InitializeAsync());
        return host;
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
            host.Journal.Input.QuickInput = "1630 1800 ревью pull request";
            await SaveAsync(window, "journal-light.png");
        }
    }

    [AvaloniaFact]
    public async Task Journal_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark);
        using (host)
        {
            host.Journal.Input.IsFormExpanded = true;
            host.Journal.Input.FormStart = new TimeSpan(22, 0, 0);
            host.Journal.Input.FormEnd = new TimeSpan(1, 30, 0);
            host.Journal.Input.FormComment = "ночная выкатка";
            await SaveAsync(window, "journal-dark.png");
        }
    }

    [AvaloniaFact]
    public async Task QuickInput_Error()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            host.Journal.Input.QuickInput = "0900 2500 созвон";
            await SaveAsync(window, "quick-input-error.png");
        }
    }

    [AvaloniaFact]
    public async Task Statistics_Month_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            host.Main.NavigateCommand.Execute("3");
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
            host.Main.NavigateCommand.Execute("3");
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
            host.Main.NavigateCommand.Execute("3");
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
            host.Main.NavigateCommand.Execute("4");
            await SaveAsync(window, "settings-light.png");
        }
    }

    [AvaloniaFact]
    public async Task About_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark);
        using (host)
        {
            host.Main.NavigateCommand.Execute("5");
            await SaveAsync(window, "about-dark.png");
        }
    }

    [AvaloniaFact]
    public async Task Journal_UndoToast_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark);
        using (host)
        {
            var rows = host.Journal.Rows.OfType<ViewModels.EntryRow>().Take(2).ToList();
            foreach (var row in rows)
            {
                host.Journal.SelectedRows.Add(row);
            }

            await host.Journal.DeleteSelectedCommand.ExecuteAsync(null);
            await SaveAsync(window, "journal-undo-dark.png");
        }
    }

    [AvaloniaFact]
    public async Task Notepad_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light, notepad: true);
        using (host)
        {
            host.Main.NavigateCommand.Execute("2");
            host.Notepad.Input.QuickInput = "1630 1800 тексты для первого экрана";
            await SaveAsync(window, "notepad-light.png");
        }
    }

    [AvaloniaFact]
    public async Task Notepad_Archive_Dark()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Dark, notepad: true);
        using (host)
        {
            host.Main.NavigateCommand.Execute("2");
            host.Notepad.ShowArchiveCommand.Execute(null);
            host.Notepad.ArchiveSheets[0].IsExpanded = true;
            await SaveAsync(window, "notepad-archive-dark.png");
        }
    }

    [AvaloniaFact]
    public async Task EditDialog_Light()
    {
        var (host, window) = await OpenAsync(ThemeVariant.Light);
        using (host)
        {
            var entry = host.Journal.Rows.OfType<ViewModels.EntryRow>().First().Entry;
            var dialog = new EditEntryWindow { DataContext = new ViewModels.EntryEditorViewModel(entry, host.Entries) };
            _ = dialog.ShowDialog<bool>(window);
            await UiTestHelpers.SettleAsync(10);
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(OutputDir, "edit-dialog-light.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            dialog.Close(false);
            window.Close();
        }
    }
}
