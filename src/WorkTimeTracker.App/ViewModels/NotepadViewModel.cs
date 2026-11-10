using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>Карточка листа в архиве блокнота (F8.15). Записи — только для чтения, в текущем их состоянии (F8.16).</summary>
public sealed partial class ArchiveSheetViewModel : ObservableObject
{
    public ArchiveSheetViewModel(NotepadSheetView view, TimeZoneInfo zone, bool isExpanded)
    {
        Id = view.Sheet.Id;
        Title = NotepadFormat.SheetTitle(view, zone);
        DateRangeText = NotepadFormat.DateRange(view);
        TotalsText = NotepadFormat.Totals(view.TotalMinutes, view.Entries.Count);
        ClearedText = NotepadFormat.Cleared(view, zone);
        AllEntriesDeleted = view.IsEmpty;
        Entries = [.. view.Entries.Select(e => new EntryRow(e))];
        IsExpanded = isExpanded;
    }

    public long Id { get; }

    /// <summary>Название листа или «Лист 3–6 нояб.».</summary>
    public string Title { get; }

    /// <summary>Диапазон дат записей или «записи удалены».</summary>
    public string DateRangeText { get; }

    /// <summary>«23 ч 15 мин · 3 записи».</summary>
    public string TotalsText { get; }

    /// <summary>«очищен 6 нояб., 18:40».</summary>
    public string ClearedText { get; }

    /// <summary>Все записи листа удалены в журнале (F8.16).</summary>
    public bool AllEntriesDeleted { get; }

    public IReadOnlyList<EntryRow> Entries { get; }

    /// <summary>«3–6 нояб. · 23 ч 15 мин · 3 записи · очищен 6 нояб., 18:40».</summary>
    public string SubtitleText => AllEntriesDeleted ? $"{TotalsText} · {ClearedText}" : $"{DateRangeText} · {TotalsText} · {ClearedText}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDeletedNote))]
    public partial bool IsExpanded { get; set; }

    public bool ShowDeletedNote => IsExpanded && AllEntriesDeleted;

    /// <summary>Клик по карточке раскрывает список записей листа.</summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public string AccessibleName => $"{Title}, {DateRangeText}, {TotalsText}, {ClearedText}";
}

/// <summary>
/// Вкладка «Блокнот» (F8): рабочий лист поверх общих записей. Добавленное здесь — обычные записи журнала;
/// блокнот помнит, что добавлено через него, показывает сумму, а «Очистить всё» переносит лист в архив.
/// Кроме добавления и правки, ни одно действие вкладки не меняет записи и статистику (ADR-0009).
/// </summary>
public sealed partial class NotepadViewModel : ObservableObject
{
    public const string EmptyCurrentText = "Блокнот пуст — запишите время, например 0900 1800 задача. Записи попадут и в журнал, и в статистику.";
    public const string EmptyArchiveText = "Здесь появятся очищенные блокноты";

    private readonly NotepadService _notepad;
    private readonly EntryService _entries;
    private readonly AppEvents _events;
    private readonly IUserInteraction _interaction;
    private readonly TimeProvider _time;
    private int _loadVersion;
    private bool _active; // до первой явной загрузки (после инициализации БД) к БД не обращаемся
    private bool _settingTitle;

    public NotepadViewModel(NotepadService notepad, EntryService entries, AppEvents events, IUserInteraction interaction, TimeProvider time)
    {
        _notepad = notepad;
        _entries = entries;
        _events = events;
        _interaction = interaction;
        _time = time;

        Input = new EntryInputViewModel(entries, time, (text, date) => notepad.AddFromTextAsync(text, date), draft => notepad.AddAsync(draft), OnAddedAsync);
        Undo = new UndoToastViewModel(time);
        Title = "";
        SummaryText = NotepadFormat.Summary(null, time.LocalTimeZone);
        StatusText = "";
        ArchiveTabText = "Архив (0)";
        IsEmpty = true;
        IsArchiveEmpty = true;
        TitleSaveTask = Task.CompletedTask;

        // Записи изменили в «Журнале» (правка, удаление, отмена удаления) — плашка и архив обновляются сразу (F8.6, F8.12).
        _events.EntriesChanged += (sender, args) =>
        {
            if (_active && !ReferenceEquals(sender, this))
            {
                _ = LoadAsync();
            }
        };
    }

    /// <summary>Тот же контрол ввода, что в «Журнале» (F8.2).</summary>
    public EntryInputViewModel Input { get; }

    /// <summary>Отмена последнего действия вкладки: ✕, «Очистить всё», «Удалить из архива» (F8.20).</summary>
    public UndoToastViewModel Undo { get; }

    // ---------- Переключатель «Текущий | Архив (N)» (F8.15) ----------

    /// <summary>0 — текущий лист, 1 — архив.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentVisible), nameof(IsArchiveVisible))]
    public partial int SelectedTabIndex { get; set; }

    public bool IsCurrentVisible => SelectedTabIndex != 1;

    public bool IsArchiveVisible => SelectedTabIndex == 1;

    [ObservableProperty]
    public partial string ArchiveTabText { get; set; }

    [RelayCommand]
    private void ShowCurrent() => SelectedTabIndex = 0;

    [RelayCommand]
    private void ShowArchive() => SelectedTabIndex = 1;

    // ---------- Плашка (F8.6, F8.7) ----------

    /// <summary>Название листа: необязательное, до 100 символов. Сохраняется при изменении (F8.7).</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public int MaxTitleLength => NotepadSheet.MaxTitleLength;

    /// <summary>Последнее сохранение названия (для тестов и аккуратного завершения).</summary>
    public Task TitleSaveTask { get; private set; }

    partial void OnTitleChanged(string value)
    {
        if (_active && !_settingTitle)
        {
            TitleSaveTask = SaveTitleAsync(value);
        }
    }

    private async Task SaveTitleAsync(string value)
    {
        await _notepad.SetTitleAsync(value);
        Undo.Invalidate();
        await LoadAsync();
    }

    private void SetTitleSilently(string value)
    {
        _settingTitle = true;
        Title = value;
        _settingTitle = false;
    }

    // ---------- Текущий лист (F8.8) ----------

    public ObservableCollection<EntryRow> Rows { get; } = [];

    /// <summary>Выделенные строки (Ctrl/Shift+клик, F8.11).</summary>
    public ObservableCollection<object> SelectedRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearAllCommand))]
    public partial bool IsEmpty { get; set; }

    public bool CanClear => !IsEmpty;

    // ---------- Архив (F8.15–F8.17) ----------

    public ObservableCollection<ArchiveSheetViewModel> ArchiveSheets { get; } = [];

    [ObservableProperty]
    public partial bool IsArchiveEmpty { get; set; }

    /// <summary>Загружает текущий лист и архив.</summary>
    public async Task LoadAsync()
    {
        _active = true;
        Input.RefreshToday();
        var version = ++_loadVersion;
        var current = await _notepad.GetCurrentAsync();
        var archive = await _notepad.GetArchiveAsync();
        if (version != _loadVersion)
        {
            return; // пришёл более свежий запрос
        }

        var zone = _time.LocalTimeZone;
        SetTitleSilently(current?.Sheet.Title ?? "");
        SummaryText = NotepadFormat.Summary(current, zone);

        Rows.Clear();
        SelectedRows.Clear();
        foreach (var e in current?.Entries ?? [])
        {
            Rows.Add(new EntryRow(e));
        }

        IsEmpty = Rows.Count == 0;

        var expanded = ArchiveSheets.Where(s => s.IsExpanded).Select(s => s.Id).ToHashSet();
        ArchiveSheets.Clear();
        foreach (var sheet in archive)
        {
            ArchiveSheets.Add(new ArchiveSheetViewModel(sheet, zone, expanded.Contains(sheet.Sheet.Id)));
        }

        IsArchiveEmpty = ArchiveSheets.Count == 0;
        ArchiveTabText = $"Архив ({ArchiveSheets.Count})";
    }

    // ---------- Действия с записями (F8.10, F8.11) ----------

    private async Task OnAddedAsync(int count)
    {
        StatusText = EntryInputViewModel.AddedText(count);
        Undo.Invalidate();
        await ReloadAndNotifyAsync();
    }

    /// <summary>Правка меняет саму запись — тот же диалог и проверки, что в «Журнале» (F8.10).</summary>
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
            Undo.Invalidate();
            await ReloadAndNotifyAsync();
        }
    }

    [RelayCommand]
    private Task RemoveEntryAsync(EntryRow? row) => row is null ? Task.CompletedTask : RemoveAsync([row.Id]);

    [RelayCommand]
    private Task RemoveSelectedAsync() => RemoveAsync([.. SelectedRows.OfType<EntryRow>().Select(r => r.Id)]);

    /// <summary>
    /// ✕ / Del: запись убирается только из блокнота и остаётся в журнале и статистике (F8.11).
    /// Полного удаления записи на этой вкладке нет намеренно (F8.13).
    /// </summary>
    private async Task RemoveAsync(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var n = await _notepad.RemoveAsync(ids);
        StatusText = "";
        Undo.Offer($"Убрано из блокнота: {n} {RuFormat.Plural(n, "запись", "записи", "записей")}", async () =>
        {
            var restored = await _notepad.UndoRemoveAsync(ids);
            StatusText = $"Возвращено в блокнот: {restored} {RuFormat.Plural(restored, "запись", "записи", "записей")}";
            await LoadAsync();
        });
        await LoadAsync();
    }

    // ---------- Очистка и архив (F8.14, F8.17) ----------

    /// <summary>«Очистить всё»: лист уходит в архив, записи остаются в журнале и статистике. Без подтверждения — с отменой.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAllAsync()
    {
        if (await _notepad.ClearAsync() is not { } sheetId)
        {
            return;
        }

        StatusText = "";
        Undo.Offer("Блокнот перенесён в архив", async () =>
        {
            StatusText = await _notepad.UndoClearAsync(sheetId)
                ? "Блокнот восстановлен"
                : "Не удалось отменить очистку: уже начат новый лист";
            await LoadAsync();
        });
        await LoadAsync();
    }

    /// <summary>«Удалить из архива»: удаляется только лист (F8.17).</summary>
    [RelayCommand]
    private async Task DeleteSheetAsync(ArchiveSheetViewModel? sheet)
    {
        if (sheet is null || !await _notepad.DeleteSheetAsync(sheet.Id))
        {
            return;
        }

        StatusText = "";
        Undo.Offer("Лист удалён из архива · записи остались в журнале", async () =>
        {
            await _notepad.UndoDeleteSheetAsync(sheet.Id);
            StatusText = "Лист возвращён в архив";
            await LoadAsync();
        });
        await LoadAsync();
    }

    /// <summary>Записи изменились (добавление, правка) — обновить «Журнал» и «Статистику».</summary>
    private async Task ReloadAndNotifyAsync()
    {
        await LoadAsync();
        _events.RaiseEntriesChanged(this);
    }
}
