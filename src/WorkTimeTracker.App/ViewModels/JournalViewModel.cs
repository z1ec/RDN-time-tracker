using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Parsing;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Statistics;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Экран «Журнал» (F7.2): быстрый ввод с предпросмотром (F1), форма (F2), сводка (F3.4),
/// список по дням, удаление с отменой (F5), редактирование и поиск (F6).
/// </summary>
public sealed partial class JournalViewModel : ObservableObject
{
    public const int PageDays = 30;
    public static readonly TimeSpan UndoToastDuration = TimeSpan.FromSeconds(8);

    private readonly EntryService _entries;
    private readonly IEntryRepository _repository;
    private readonly SettingsService _settings;
    private readonly AppEvents _events;
    private readonly IUserInteraction _interaction;
    private readonly TimeProvider _time;
    private IReadOnlyList<long> _undoIds = [];
    private int _toastVersion;
    private int _loadVersion;
    private bool _active; // до первой явной загрузки (после инициализации БД) к БД не обращаемся

    public JournalViewModel(EntryService entries, IEntryRepository repository, SettingsService settings, AppEvents events, IUserInteraction interaction, TimeProvider time)
    {
        _entries = entries;
        _repository = repository;
        _settings = settings;
        _events = events;
        _interaction = interaction;
        _time = time;

        QuickInput = "";
        PreviewText = "";
        FormComment = "";
        FormDurationText = "";
        SearchText = "";
        SummaryText = "";
        UndoToastText = "";
        EmptyText = "";
        StatusText = "";
        DaysLoaded = PageDays;
        SelectedDate = _entries.Today.ToDateTime(TimeOnly.MinValue);

        _events.EntriesChanged += (sender, args) =>
        {
            if (_active && !ReferenceEquals(sender, this))
            {
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

    // ---------- Быстрый ввод (F1) ----------

    [ObservableProperty]
    public partial string QuickInput { get; set; }

    /// <summary>Дата для ввода без даты и для формы (по умолчанию — сегодня).</summary>
    [ObservableProperty]
    public partial DateTime? SelectedDate { get; set; }

    [ObservableProperty]
    public partial string PreviewText { get; set; }

    [ObservableProperty]
    public partial bool PreviewIsError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool CanAdd { get; set; }

    public bool HasPreview => PreviewText.Length > 0;

    /// <summary>Последний доступный для выбора день (будущие даты запрещены).</summary>
    public DateTime MaxDate => _entries.Today.ToDateTime(TimeOnly.MinValue);

    public DateOnly SelectedDay => SelectedDate is { } d ? DateOnly.FromDateTime(d) : _entries.Today;

    public string SelectedDateLabel => SelectedDay == _entries.Today ? "Сегодня" : RuFormat.DayMonthShort(SelectedDay);

    partial void OnQuickInputChanged(string value) => UpdatePreview();

    partial void OnPreviewTextChanged(string value) => OnPropertyChanged(nameof(HasPreview));

    partial void OnSelectedDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(SelectedDay));
        OnPropertyChanged(nameof(SelectedDateLabel));
        UpdatePreview();
        UpdateFormPreview();
    }

    /// <summary>Живой предпросмотр (F1.3): «✓ 10 нояб. · 09:00 → 18:00 · 9 ч 00 мин · «созвон»» или ошибка.</summary>
    public void UpdatePreview()
    {
        var batch = _entries.Parser.ParseMany(QuickInput, SelectedDay);
        if (batch.IsEmpty)
        {
            PreviewText = "";
            PreviewIsError = false;
            CanAdd = false;
            return;
        }

        if (!batch.IsSuccess)
        {
            PreviewText = "✗ " + string.Join(Environment.NewLine + "✗ ", batch.ErrorMessages);
            PreviewIsError = true;
            CanAdd = false;
            return;
        }

        PreviewIsError = false;
        CanAdd = true;
        if (batch.Lines.Count == 1)
        {
            var d = batch.Lines[0].Draft;
            var text = $"✓ {RuFormat.DayMonthShort(d.Date)} · {RuFormat.Clock(d.Range.StartMinute)} → {RuFormat.ClockWithDayOffset(d.Range.EndMinute)} · {DurationFormatter.Format(d.Range.DurationMinutes)}";
            PreviewText = d.Comment.Length > 0 ? $"{text} · «{d.Comment}»" : text;
        }
        else
        {
            var total = batch.Lines.Sum(l => l.Draft.Range.DurationMinutes);
            var n = batch.Lines.Count;
            PreviewText = $"✓ {n} {RuFormat.Plural(n, "запись", "записи", "записей")} · всего {DurationFormatter.Format(total)}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var result = await _entries.AddFromTextAsync(QuickInput, SelectedDay);
        if (!result.IsSuccess)
        {
            PreviewText = "✗ " + string.Join(Environment.NewLine + "✗ ", result.Errors);
            PreviewIsError = true;
            CanAdd = false;
            return;
        }

        QuickInput = "";
        StatusText = $"Добавлено: {result.Entries.Count} {RuFormat.Plural(result.Entries.Count, "запись", "записи", "записей")}";
        await AfterChangeAsync();
    }

    /// <summary>Esc в поле ввода: очистить (F7.6).</summary>
    [RelayCommand]
    private void ClearInput()
    {
        QuickInput = "";
        UpdatePreview();
    }

    [RelayCommand]
    private void ResetDate() => SelectedDate = _entries.Today.ToDateTime(TimeOnly.MinValue);

    // ---------- Форма (F2) ----------

    [ObservableProperty]
    public partial bool IsFormExpanded { get; set; }

    [ObservableProperty]
    public partial TimeSpan? FormStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? FormEnd { get; set; }

    [ObservableProperty]
    public partial string FormComment { get; set; }

    [ObservableProperty]
    public partial string FormDurationText { get; set; }

    [ObservableProperty]
    public partial string? FormError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromFormCommand))]
    public partial bool CanAddFromForm { get; set; }

    public string FormCommentCounter => $"{FormComment.Length}/{EntryRules.MaxCommentLength}";

    partial void OnFormStartChanged(TimeSpan? value) => UpdateFormPreview();

    partial void OnFormEndChanged(TimeSpan? value) => UpdateFormPreview();

    partial void OnFormCommentChanged(string value)
    {
        OnPropertyChanged(nameof(FormCommentCounter));
        UpdateFormPreview();
    }

    /// <summary>Живой расчёт длительности формы и отметка «+1 день» (F2.4).</summary>
    public void UpdateFormPreview()
    {
        if (FormStart is null || FormEnd is null)
        {
            FormDurationText = "";
            FormError = null;
            CanAddFromForm = false;
            return;
        }

        var draft = _entries.BuildDraft(SelectedDay, FormStart.Value, FormEnd.Value, FormComment);
        FormError = draft.IsSuccess ? null : draft.Error;
        FormDurationText = draft.IsSuccess ? FormHelpers.DurationWithNextDay(draft.Value!.Range) : "";
        CanAddFromForm = draft.IsSuccess;
    }

    [RelayCommand]
    private void SetFormStartNow() => FormStart = NowTime();

    [RelayCommand]
    private void SetFormEndNow() => FormEnd = NowTime();

    [RelayCommand(CanExecute = nameof(CanAddFromForm))]
    private async Task AddFromFormAsync()
    {
        var draft = _entries.BuildDraft(SelectedDay, FormStart ?? TimeSpan.Zero, FormEnd ?? TimeSpan.Zero, FormComment);
        if (!draft.IsSuccess)
        {
            FormError = draft.Error;
            return;
        }

        var result = await _entries.AddAsync(draft.Value!);
        if (!result.IsSuccess)
        {
            FormError = string.Join(Environment.NewLine, result.Errors);
            CanAddFromForm = false;
            return;
        }

        FormStart = null;
        FormEnd = null;
        FormComment = "";
        StatusText = "Добавлено: 1 запись";
        await AfterChangeAsync();
    }

    private TimeSpan NowTime()
    {
        var now = _time.GetLocalNow();
        return new TimeSpan(now.Hour, now.Minute, 0);
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
        OnPropertyChanged(nameof(MaxDate));
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

    [ObservableProperty]
    public partial bool IsUndoToastVisible { get; set; }

    [ObservableProperty]
    public partial string UndoToastText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; set; }

    [RelayCommand]
    private Task DeleteEntryAsync(EntryRow? row) => row is null ? Task.CompletedTask : DeleteAsync([row.Id]);

    [RelayCommand]
    private Task DeleteSelectedAsync() => DeleteAsync([.. SelectedRows.OfType<EntryRow>().Select(r => r.Id)]);

    private async Task DeleteAsync(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var n = await _entries.DeleteAsync(ids);
        _undoIds = ids;
        CanUndo = true;
        UndoToastText = $"Удалено записей: {n}";
        IsUndoToastVisible = true;
        var version = ++_toastVersion;
        await ReloadAndNotifyAsync();
        _ = HideToastLaterAsync(version);
    }

    private async Task HideToastLaterAsync(int version)
    {
        await Task.Delay(UndoToastDuration, _time);
        if (version == _toastVersion)
        {
            IsUndoToastVisible = false;
        }
    }

    /// <summary>
    /// Отмена последнего удаления (кнопка в уведомлении или Ctrl+Z, F5.3). Доступна, пока после удаления
    /// не было других изменений: так восстановление не может создать пересечение.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        var ids = _undoIds;
        _undoIds = [];
        CanUndo = false;
        IsUndoToastVisible = false;
        _toastVersion++;
        var n = await _entries.UndoDeleteAsync(ids);
        StatusText = $"Восстановлено: {n} {RuFormat.Plural(n, "запись", "записи", "записей")}";
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
        // Любое изменение после удаления делает отмену недоступной (см. UndoAsync).
        _undoIds = [];
        CanUndo = false;
        IsUndoToastVisible = false;
        await ReloadAndNotifyAsync();
    }

    private async Task ReloadAndNotifyAsync()
    {
        await LoadAsync();
        _events.RaiseEntriesChanged(this);
    }
}
