using Avalonia.Headless.XUnit;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Data;
using static WorkTimeTracker.App.Tests.JournalViewModelTests;

namespace WorkTimeTracker.App.Tests;

/// <summary>F7.4, F7.5, F4.5, F7.3: настройки, «О программе», запуск и навигация.</summary>
public class SettingsAndAboutTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task Settings_ArePersisted()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Settings;
        Assert.Equal(new TimeSpan(8, 0, 0), vm.DailyNorm);
        Assert.Equal(5, vm.WorkDays.Count(d => d.IsSelected));
        Assert.Equal("Пн", vm.WorkDays[0].Title);
        Assert.Equal("Понедельник", vm.WorkDays[0].FullName);

        vm.DailyNorm = new TimeSpan(7, 30, 0);
        vm.WorkDays[5].IsSelected = true; // суббота
        vm.ThemeIndex = 2;

        await WaitAsync(() => host.Services.GetService(typeof(SettingsStore)) is SettingsStore);
        var store = (SettingsStore)host.Services.GetService(typeof(SettingsStore))!;
        await WaitAsync(() => store.LoadAsync(_ct).Result is { DailyNormMinutes: 450, Theme: ThemePreference.Dark } s && s.WorkDays.Contains(DayOfWeek.Saturday));
    }

    [AvaloniaFact]
    public async Task Settings_DataLocation_AndFolders()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Settings;
        Assert.Equal(host.Paths.DatabasePath, vm.DatabasePath);
        Assert.Equal(host.Paths.DataDirectory, vm.DataDirectory);
        Assert.Equal("Папка задана параметром --data-dir", vm.LocationKindText);
        Assert.Equal(3, vm.Themes.Count);

        vm.OpenDataFolderCommand.Execute(null);
        vm.OpenBackupsFolderCommand.Execute(null);
        Assert.Equal([host.Paths.DataDirectory, host.Paths.BackupsDirectory], host.Interaction.OpenedFolders);
    }

    [Theory]
    [InlineData(DataLocationKind.Portable, "Портативный режим: рядом с программой лежит portable.txt")]
    [InlineData(DataLocationKind.Default, "Стандартная папка пользователя")]
    public void Settings_LocationKinds(DataLocationKind kind, string text)
    {
        using var host = new TestHost();
        var vm = new SettingsViewModel(
            (SettingsService)host.Services.GetService(typeof(SettingsService))!,
            new ThemeService(),
            (BackupService)host.Services.GetService(typeof(BackupService))!,
            host.Repository,
            host.Paths with { Kind = kind },
            host.Interaction);
        Assert.Equal(text, vm.LocationKindText);
    }

    [AvaloniaFact]
    public async Task Settings_BackupNow_AndExportAll()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Settings;
        Assert.StartsWith("Копий пока нет", vm.BackupsInfo, StringComparison.Ordinal);

        await vm.BackupNowCommand.ExecuteAsync(null);
        Assert.Equal("Резервная копия сохранена: worktime-2026-11-10.db", vm.StatusText);
        Assert.Equal("Копий: 1 из 14, последняя — worktime-2026-11-10.db", vm.BackupsInfo);

        host.Journal.QuickInput = "0900 1000\n1100 1200";
        await host.Journal.AddCommand.ExecuteAsync(null);
        host.Interaction.SavePath = Path.Combine(host.Dir, "all.csv");
        await vm.ExportAllCommand.ExecuteAsync(null);
        Assert.Equal("Экспортировано: 2 записи в all.csv", vm.StatusText);
        Assert.True(File.Exists(host.Interaction.SavePath));

        host.Interaction.SavePath = null;
        await vm.ExportAllCommand.ExecuteAsync(null);
        host.Interaction.SavePath = Path.Combine(host.Dir, "нет", "x.csv");
        await vm.ExportAllCommand.ExecuteAsync(null);
        Assert.Single(host.Interaction.Errors);
    }

    [AvaloniaFact]
    public async Task About_ShowsVersionAndLinks()
    {
        using var host = await TestHost.StartAsync();
        var vm = (AboutViewModel)host.Main.Navigation[3].Page;
        Assert.Equal("WorkTime Tracker", vm.Title);
        Assert.StartsWith("Версия ", vm.Version, StringComparison.Ordinal);
        Assert.Equal("https://github.com/z1ec/RDN-time-tracker", vm.RepositoryUrl);
        Assert.Equal(host.Paths.DataDirectory, vm.DataDirectory);
        Assert.Contains("MIT", vm.License, StringComparison.Ordinal);
        Assert.Contains("без телеметрии", vm.Privacy, StringComparison.Ordinal);
        Assert.NotEmpty(vm.Description);
        vm.OpenRepositoryCommand.Execute(null);
        vm.OpenDataFolderCommand.Execute(null);
        Assert.Equal([AppInfo.RepositoryUrl], host.Interaction.OpenedUrls);
        Assert.Equal([host.Paths.DataDirectory], host.Interaction.OpenedFolders);
    }

    [AvaloniaFact]
    public async Task Main_Navigation()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Main;
        Assert.True(vm.IsReady);
        Assert.Same(host.Journal, vm.CurrentPage);
        vm.NavigateCommand.Execute("2");
        Assert.Same(host.Statistics, vm.CurrentPage);
        vm.NavigateCommand.Execute("3");
        Assert.Same(host.Settings, vm.CurrentPage);
        vm.NavigateCommand.Execute("9");
        vm.NavigateCommand.Execute("x");
        Assert.Same(host.Settings, vm.CurrentPage);
        Assert.Equal(["Журнал", "Статистика", "Настройки", "О программе"], vm.Navigation.Select(n => n.Title));
        vm.TogglePaneCommand.Execute(null);
        Assert.True(vm.IsPaneOpen);
    }

    [AvaloniaFact]
    public async Task Main_CorruptDatabase_ShowsFriendlyError_AndKeepsFile()
    {
        using var host = new TestHost();
        Directory.CreateDirectory(host.Dir);
        var garbage = new byte[8192];
        new Random(1).NextBytes(garbage);
        await File.WriteAllBytesAsync(host.Paths.DatabasePath, garbage, _ct);

        Assert.False(await host.Main.InitializeAsync());

        var error = Assert.Single(host.Interaction.Errors);
        Assert.Equal("Не удалось открыть базу данных", error.Title);
        Assert.Contains(host.Paths.DatabasePath, error.Message, StringComparison.Ordinal);
        Assert.Equal(host.Paths.DataDirectory, error.Folder);
        Assert.Equal(garbage, await File.ReadAllBytesAsync(host.Paths.DatabasePath, _ct));
        Assert.False(host.Main.IsReady);
    }

    [Fact]
    public void ThemeService_Maps()
    {
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, ThemeService.ToVariant(ThemePreference.Light));
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, ThemeService.ToVariant(ThemePreference.Dark));
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, ThemeService.ToVariant(ThemePreference.System));
    }
}
