using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Statistics;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Экран «Журнал» (F7.2): быстрый ввод с предпросмотром (F1), форма (F2), сводка (F3.4),
/// список по дням, удаление с отменой (F5), редактирование и поиск (F6).
/// </summary>
public sealed partial class JournalViewModel : ObservableObject
{
    public const int PageDays = 30;

    private readonly EntryService _entries;
    private readonly IEntryRepository _repository;
    private readonly SettingsService _settings;
    private readonly AppEvents _events;
    private readonly IUserInteraction _interaction;
    private int _loadVersion;
    private bool _active; // до первой явной загрузки (после инициализации БД) к БД не обращаемся

    public JournalViewModel(EntryService entries, IEntryRepository repository, SettingsService settings, AppEvents events, IUserInteraction interaction, TimeProvider time)
    {
        _entries = entries;
        _repository = repository;
        _settings = settings;
        _events = events;
        _interaction = interaction;

        Input = new EntryInputViewModel(entries, time, (text, date) => entries.AddFromTextAsync(text, date), draft => entries.AddAsync(draft), OnAddedAsync);
        Undo = new UndoToastViewModel(time);
        SearchText = "";
        SummaryText = "";
        EmptyText = "";
        StatusText = "";
        DaysLoaded = PageDays;

        _events.EntriesChanged += (sender, args) =>
        {
            if (_active && !ReferenceEquals(sender, this))
            {
                // Записи изменили в другом месте (блокнот): отмена удаления могла бы создать пересечение.
                Undo.Invalidate();
                _ = LoadAsync();
            }
        };
        _settings.Changed += (sender, args) =>
        {
            if (_active)
            {
                _ = LoadSummaryAsync();
            }
        };
    }

    /// <summary>Быстрый ввод (F1) и форма (F2) — общий с «Блокнотом» контрол.</summary>
    public EntryInputViewModel Input { get; }

    /// <summary>Уведомление «Отменить» и Ctrl+Z для последнего удаления (F5.3).</summary>
    public UndoToastViewModel Undo { get; }

    private async Task OnAddedAsync(int count)
    {
        StatusText = EntryInputViewModel.AddedText(count);
        await AfterChangeAsync();
    }

    // ---------- Сводка (F3.4) ----------

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    // ---------- Список (F7.2) ----------

    public ObservableCollection<JournalRow> Rows { get; } = [];

    /// <summary>Выделенные строки (F5.2: Ctrl/Shift+клик).</summary>
    public ObservableCollection<object> SelectedRows { get; } = [];

    [ObservableProperty]
    public partial int DaysLoaded { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        if (_active)
        {
            _ = LoadListAsync();
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private async Task ShowMoreAsync()
    {
        DaysLoaded += PageDays;
        await LoadListAsync();
    }

    /// <summary>Загружает сводку и список.</summary>
    public async Task LoadAsync()
    {
        _active = true;
        Input.RefreshToday();
        await Task.WhenAll(LoadSummaryAsync(), LoadListAsync());
    }

    private async Task LoadSummaryAsync()
    {
        var today = _entries.Today;
        var week = PeriodNavigator.ForDate(PeriodKind.Week, today);
        var entries = await _repository.GetRangeAsync(week.Start, today);
        var todayTotal = entries.Where(e => e.Date == today).Sum(e => e.DurationMinutes);
        var weekTotal = entries.Sum(e => e.DurationMinutes);
        var settings = _settings.Current;
        var weekNorm = week.Days().Count(d => settings.WorkDays.Contains(d.DayOfWeek)) * settings.DailyNormMinutes;
        SummaryText = $"Сегодня: {DurationFormatter.Format(todayTotal)} · Неделя: {DurationFormatter.Format(weekTotal)} из {DurationFormatter.FormatShort(weekNorm)}";
    }

    private async Task LoadListAsync()
    {
        var version = ++_loadVersion;
        var today = _entries.Today;
        IReadOnlyList<WorkEntry> entries;
        var hasMore = false;
        if (IsSearching)
        {
            entries = await _repository.SearchAsync(SearchText);
        }
        else
        {
            var from = today.AddDays(-(DaysLoaded - 1));
            entries = await _repository.GetRangeAsync(from, today);
            var first = await _repository.GetFirstDateAsync();
            hasMore = first is { } f && f < from;
        }

        if (version != _loadVersion)
        {
            return; // пришёл более свежий запрос
        }

        Rows.Clear();
        SelectedRows.Clear();
        foreach (var day in entries.GroupBy(e => e.Date).OrderByDescending(g => g.Key))
        {
            Rows.Add(new DayHeaderRow(day.Key, RuFormat.GroupHeader(day.Key, today), day.Sum(e => e.DurationMinutes), day.Key == today));
            foreach (var e in day.OrderBy(e => e.StartMinute))
            {
                Rows.Add(new EntryRow(e));
            }
        }

        HasMore = hasMore;
        IsEmpty = Rows.Count == 0;
        EmptyText = IsSearching ? $"Ничего не найдено по запросу «{SearchText.Trim()}»" : "Пока нет записей — введите, например, 0900 1800 планёрка";
    }

    // ---------- Удаление и отмена (F5) ----------

    [RelayCommand]
    private Task DeleteEntryAsync(EntryRow? row) => row is null ? Task.CompletedTask : DeleteAsync([row.Id]);

    [RelayCommand]
    private Task DeleteSelectedAsync() => DeleteAsync([.. SelectedRows.OfType<EntryRow>().Select(r => r.Id)]);

    /// <summary>
    /// Мягкое удаление с отменой (кнопка в уведомлении или Ctrl+Z, F5.3). Запись исчезает отовсюду, включая
    /// блокнот и его архив (F8.12). Отмена доступна, пока после удаления не было других изменений записей:
    /// так восстановление не может создать пересечение.
    /// </summary>
    private async Task DeleteAsync(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var n = await _entries.DeleteAsync(ids);
        Undo.Offer($"Удалено записей: {n}", async () =>
        {
            var restored = await _entries.UndoDeleteAsync(ids);
            StatusText = $"Восстановлено: {restored} {RuFormat.Plural(restored, "запись", "записи", "записей")}";
            await ReloadAndNotifyAsync();
        });
        await ReloadAndNotifyAsync();
    }

    // ---------- Редактирование (F6.3) ----------

    [RelayCommand]
    private async Task EditEntryAsync(EntryRow? row)
    {
        if (row is null)
        {
            return;
        }

        var editor = new EntryEditorViewModel(row.Entry, _entries);
        if (await _interaction.EditEntryAsync(editor))
        {
            StatusText = "Запись изменена";
            await AfterChangeAsync();
        }
    }

    private async Task AfterChangeAsync()
    {
        // Любое изменение после удаления делает отмену недоступной (см. DeleteAsync).
        Undo.Invalidate();
        await ReloadAndNotifyAsync();
    }

    private async Task ReloadAndNotifyAsync()
    {
        await LoadAsync();
        _events.RaiseEntriesChanged(this);
    }
}
