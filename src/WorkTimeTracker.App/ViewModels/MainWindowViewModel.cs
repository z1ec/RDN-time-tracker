using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>Пункт боковой навигации (F7.3).</summary>
public sealed record NavigationItem(string Title, string IconKey, string Hotkey, object Page);

/// <summary>Главное окно: навигация и запуск (подготовка БД, загрузка настроек и данных).</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly DatabaseInitializer _initializer;
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly IUserInteraction _interaction;
    private readonly DataPaths _paths;

    public MainWindowViewModel(
        JournalViewModel journal,
        NotepadViewModel notepad,
        StatisticsViewModel statistics,
        SettingsViewModel settingsPage,
        AboutViewModel about,
        DatabaseInitializer initializer,
        SettingsService settings,
        ThemeService theme,
        IUserInteraction interaction,
        DataPaths paths)
    {
        Journal = journal;
        Notepad = notepad;
        Statistics = statistics;
        SettingsPage = settingsPage;
        About = about;
        _initializer = initializer;
        _settings = settings;
        _theme = theme;
        _interaction = interaction;
        _paths = paths;
        Navigation =
        [
            new("Журнал", "IconBook", "Ctrl+1", journal),
            new("Блокнот", "IconNotepad", "Ctrl+2", notepad),
            new("Статистика", "IconDataBarVertical", "Ctrl+3", statistics),
            new("Настройки", "IconSettings", "Ctrl+4", settingsPage),
            new("О программе", "IconInfo", "Ctrl+5", about),
        ];
        SelectedNavigation = Navigation[0];
        CurrentPage = journal;
    }

    public JournalViewModel Journal { get; }

    public NotepadViewModel Notepad { get; }

    public StatisticsViewModel Statistics { get; }

    public SettingsViewModel SettingsPage { get; }

    public AboutViewModel About { get; }

    public IReadOnlyList<NavigationItem> Navigation { get; }

    [ObservableProperty]
    public partial NavigationItem? SelectedNavigation { get; set; }

    [ObservableProperty]
    public partial object CurrentPage { get; set; }

    [ObservableProperty]
    public partial bool IsPaneOpen { get; set; }

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    partial void OnSelectedNavigationChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }

    /// <summary>Переход по горячим клавишам Ctrl+1…5 (F7.6, F8.1): параметр — номер раздела с 1.</summary>
    [RelayCommand]
    private void Navigate(string? number)
    {
        if (int.TryParse(number, out var n) && n >= 1 && n <= Navigation.Count)
        {
            SelectedNavigation = Navigation[n - 1];
        }
    }

    /// <summary>
    /// Отмена для Ctrl+Z (F8.20): последнее действие активной вкладки. У «Журнала» и «Блокнота» отмена своя,
    /// на остальных вкладках отменять нечего.
    /// </summary>
    public UndoToastViewModel? ActiveUndo => CurrentPage switch
    {
        JournalViewModel journal => journal.Undo,
        NotepadViewModel notepad => notepad.Undo,
        _ => null,
    };

    [RelayCommand]
    private void TogglePane() => IsPaneOpen = !IsPaneOpen;

    /// <summary>
    /// Подготовка при запуске: БД (миграции, бэкап, очистка) → настройки → тема → данные.
    /// Возвращает <c>false</c>, если БД открыть не удалось (пользователю показан диалог, F4.7).
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            var startup = await _initializer.InitializeAsync();
            Serilog.Log.Information(
                "БД готова: {Path}, схема {From}→{To}, бэкап {Backup}, очищено записей {Purged}, привязок блокнота {Items}, листов {Sheets}",
                _paths.DatabasePath, startup.Migration.FromVersion, startup.Migration.ToVersion, startup.AutoBackupPath ?? "—", startup.PurgedEntries, startup.PurgedNotepad.Items, startup.PurgedNotepad.Sheets);
        }
        catch (DatabaseOpenException ex)
        {
            Serilog.Log.Error(ex, "Не удалось открыть БД");
            await _interaction.ShowErrorAsync(
                "Не удалось открыть базу данных",
                $"{ex.InnerException?.Message}\n\nФайл: {ex.DatabasePath}\n\nДанные не изменены и не удалены. Закройте программу, проверьте файл (или восстановите его из папки backups) и запустите снова.",
                Path.GetDirectoryName(ex.DatabasePath));
            return false;
        }

        await _settings.LoadAsync();
        _theme.Apply(_settings.Current.Theme);
        await Task.WhenAll(Journal.LoadAsync(), Notepad.LoadAsync(), Statistics.LoadAsync());
        IsReady = true;
        return true;
    }
}
