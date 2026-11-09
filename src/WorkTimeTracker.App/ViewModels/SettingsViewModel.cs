using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Export;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>Переключатель рабочего дня недели (Пн…Вс).</summary>
public sealed partial class WorkDayOption : ObservableObject
{
    private readonly Action _changed;

    public WorkDayOption(DayOfWeek day, bool isSelected, Action changed)
    {
        Day = day;
        _changed = changed;
        IsSelected = isSelected;
    }

    public DayOfWeek Day { get; }

    public string Title => RuFormat.DayShort(Day);

    public string FullName => RuFormat.Capitalize(RuFormat.DayFull(Day));

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _changed();
}

/// <summary>Экран «Настройки» (F7.4): норма, рабочие дни, тема, данные, бэкап, экспорт.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly BackupService _backups;
    private readonly IEntryRepository _repository;
    private readonly IUserInteraction _interaction;
    private bool _loading;

    public SettingsViewModel(SettingsService settings, ThemeService theme, BackupService backups, IEntryRepository repository, DataPaths paths, IUserInteraction interaction)
    {
        _settings = settings;
        _theme = theme;
        _backups = backups;
        _repository = repository;
        _interaction = interaction;
        Paths = paths;
        StatusText = "";
        WorkDays = [.. RuFormat.WeekDaysMondayFirst.Select(d => new WorkDayOption(d, AppSettings.DefaultWorkDays.Contains(d), OnWorkDaysChanged))];
        _settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public DataPaths Paths { get; }

    public string DataDirectory => Paths.DataDirectory;

    public string DatabasePath => Paths.DatabasePath;

    public string LocationKindText => Paths.Kind switch
    {
        DataLocationKind.Portable => "Портативный режим: рядом с программой лежит portable.txt",
        DataLocationKind.CommandLine => "Папка задана параметром --data-dir",
        _ => "Стандартная папка пользователя",
    };

    public IReadOnlyList<string> Themes { get; } = ["Как в системе", "Светлая", "Тёмная"];

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>Дневная норма (F3.2) как время суток для TimePicker.</summary>
    [ObservableProperty]
    public partial TimeSpan? DailyNorm { get; set; }

    public ObservableCollection<WorkDayOption> WorkDays { get; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public string BackupsInfo
    {
        get
        {
            var list = _backups.ListBackups();
            return list.Count == 0
                ? $"Копий пока нет. Хранятся последние {BackupService.KeepCount}."
                : $"Копий: {list.Count} из {BackupService.KeepCount}, последняя — {Path.GetFileName(list[0])}";
        }
    }

    private void Refresh()
    {
        _loading = true;
        var s = _settings.Current;
        ThemeIndex = (int)s.Theme;
        DailyNorm = TimeSpan.FromMinutes(Math.Min(s.DailyNormMinutes, (24 * 60) - 1));
        foreach (var option in WorkDays)
        {
            option.IsSelected = s.WorkDays.Contains(option.Day);
        }

        OnPropertyChanged(nameof(BackupsInfo));
        _loading = false;
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        var theme = (ThemePreference)Math.Clamp(value, 0, 2);
        _theme.Apply(theme);
        _ = _settings.UpdateAsync(s => s with { Theme = theme });
    }

    partial void OnDailyNormChanged(TimeSpan? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        var minutes = (int)value.Value.TotalMinutes;
        _ = _settings.UpdateAsync(s => s with { DailyNormMinutes = minutes });
    }

    private void OnWorkDaysChanged()
    {
        if (_loading || WorkDays is null)
        {
            return;
        }

        var days = WorkDays.Where(o => o.IsSelected).Select(o => o.Day).ToHashSet();
        _ = _settings.UpdateAsync(s => s.WorkDays.SetEquals(days) ? s : s with { WorkDays = days });
    }

    [RelayCommand]
    private void OpenDataFolder() => _interaction.OpenFolder(Paths.DataDirectory);

    [RelayCommand]
    private void OpenBackupsFolder()
    {
        Directory.CreateDirectory(Paths.BackupsDirectory);
        _interaction.OpenFolder(Paths.BackupsDirectory);
    }

    /// <summary>«Сделать резервную копию сейчас» (F4.5).</summary>
    [RelayCommand]
    private async Task BackupNowAsync()
    {
        try
        {
            var path = await _backups.BackupNowAsync();
            StatusText = $"Резервная копия сохранена: {Path.GetFileName(path)}";
            OnPropertyChanged(nameof(BackupsInfo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Serilog.Log.Error(ex, "Ручной бэкап не удался");
            await _interaction.ShowErrorAsync("Не удалось сделать резервную копию", ex.Message, Paths.BackupsDirectory);
        }
    }

    /// <summary>Экспорт всех данных в CSV (F7.4).</summary>
    [RelayCommand]
    private async Task ExportAllAsync()
    {
        var path = await _interaction.PickSaveFileAsync("Экспорт всех данных в CSV", "worktime-all.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var entries = await _repository.GetAllAsync();
            await File.WriteAllBytesAsync(path, CsvExporter.ToBytes(entries));
            StatusText = $"Экспортировано: {entries.Count} {RuFormat.Plural(entries.Count, "запись", "записи", "записей")} в {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Экспорт всех данных не удался");
            await _interaction.ShowErrorAsync("Не удалось сохранить файл", ex.Message);
        }
    }
}
