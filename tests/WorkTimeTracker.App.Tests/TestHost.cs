using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App.Tests;

/// <summary>
/// Приложение без окна: временная папка данных, фиксированные часы, фейковые диалоги.
/// «Сегодня» по умолчанию — вторник 10.11.2026, 14:37.
/// </summary>
public sealed class TestHost : IDisposable
{
    public static readonly DateOnly DefaultToday = new(2026, 11, 10);

    public TestHost(DateTimeOffset? now = null, string? dataDir = null, bool keepExisting = false)
    {
        Dir = dataDir ?? Path.Combine(Path.GetTempPath(), "wtt-app-tests", Guid.NewGuid().ToString("N"));
        if (dataDir is not null && !keepExisting && Directory.Exists(dataDir))
        {
            Directory.Delete(dataDir, recursive: true);
        }

        Paths = new DataPaths(Dir, DataLocationKind.CommandLine);
        Time = new FakeTimeProvider(now ?? new DateTimeOffset(2026, 11, 10, 14, 37, 0, TimeSpan.Zero));
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Services = AppComposition.Build(Paths, Time, Interaction);
    }

    public string Dir { get; }

    /// <summary>Оставить папку данных после <see cref="Dispose"/> — для тестов «перезапуска» на той же БД.</summary>
    public bool KeepDataOnDispose { get; set; }

    public DataPaths Paths { get; }

    public FakeTimeProvider Time { get; }

    public FakeUserInteraction Interaction { get; } = new();

    public ServiceProvider Services { get; }

    public DateOnly Today => DateOnly.FromDateTime(Time.GetLocalNow().DateTime);

    public MainWindowViewModel Main => Services.GetRequiredService<MainWindowViewModel>();

    public JournalViewModel Journal => Services.GetRequiredService<JournalViewModel>();

    public NotepadViewModel Notepad => Services.GetRequiredService<NotepadViewModel>();

    public NotepadService NotepadService => Services.GetRequiredService<NotepadService>();

    public StatisticsViewModel Statistics => Services.GetRequiredService<StatisticsViewModel>();

    public SettingsViewModel Settings => Services.GetRequiredService<SettingsViewModel>();

    public EntryService Entries => Services.GetRequiredService<EntryService>();

    public IEntryRepository Repository => Services.GetRequiredService<IEntryRepository>();

    public static async Task<TestHost> StartAsync(DateTimeOffset? now = null, bool demoData = false, string? dataDir = null, bool keepExisting = false)
    {
        var host = new TestHost(now, dataDir, keepExisting);
        if (demoData)
        {
            // Демо-данные пишутся до запуска UI, поэтому БД готовится заранее.
            await host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            await DemoData.SeedAsync(host.Entries, host.Today);
        }

        Assert.True(await host.Main.InitializeAsync());
        return host;
    }

    public void Dispose()
    {
        Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (KeepDataOnDispose)
        {
            return;
        }

        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
