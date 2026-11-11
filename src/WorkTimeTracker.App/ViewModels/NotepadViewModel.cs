using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Карточка листа в архиве блокнота (F8.15). Строки — только для чтения; статусы и обе суммы — по тем же правилам,
/// что у текущего листа (F9.9); связанные строки получают правки из «Журнала» (F8.16, F9.6).
/// </summary>
public sealed partial class ArchiveSheetViewModel : ObservableObject
{
    public ArchiveSheetViewModel(NotepadSheetView view, TimeZoneInfo zone, bool isExpanded)
    {
        Id = view.Sheet.Id;
        Title = NotepadFormat.SheetTitle(view, zone);
        DateRangeText = NotepadFormat.DateRange(view);
        TotalsText = NotepadFormat.Totals(view);
        ClearedText = NotepadFormat.Cleared(view, zone);
        AllEntriesDeleted = view.IsEmpty;
        Entries = [.. view.Lines.Select(l => new NotepadLineRow(l))];
        IsExpanded = isExpanded;
    }

    public long Id { get; }

    /// <summary>Название листа или «Лист 3–6 нояб.».</summary>
    public string Title { get; }

    /// <summary>Диапазон дат записей или «записи удалены».</summary>
    public string DateRangeText { get; }

    /// <summary>«23 ч 15 мин · 3 записи» или «7 ч 00 мин · в статистике: 4 ч 00 мин · 2 записи» (F9.4).</summary>
    public string TotalsText { get; }

    /// <summary>«очищен 6 нояб., 18:40».</summary>
    public string ClearedText { get; }

    /// <summary>У листа не осталось видимых строк: все связанные записи удалены в журнале (F8.16).</summary>
    public bool AllEntriesDeleted { get; }

    public IReadOnlyList<NotepadLineRow> Entries { get; }

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
/// Вкладка «Блокнот» (F8, F9): песочница со своими строками. Строка без пересечений с журналом сразу становится
/// обычной записью журнала и статистики; с пересечением — остаётся только в блокноте (ADR-0010). Плашка показывает
/// сумму всех строк и, если отличается, сумму в статистике; «Очистить всё» переносит лист в архив.
/// Кроме добавления и правки, ни одно действие вкладки не меняет записи и статистику (ADR-0009, ADR-0010).
/// </summary>
public sealed partial class NotepadViewModel : ObservableObject
{
    public const string EmptyCurrentText = "Блокнот пуст — запишите время, например 0900 1800 задача. Записи без пересечений попадут и в журнал, и в статистику.";

    /// <summary>Пояснение в диалоге правки строки (F9.5).</summary>
    public const string EditHint = "Если новое время пересечётся с другой записью журнала, строка изменится только в блокноте.";
    public const string EmptyArchiveText = "Здесь появятся очищенные блокноты";

    private readonly NotepadService _notepad;
    private readonly EntryService _entries;
    private readonly AppEvents _events;
    private readonly IUserInteraction _interaction;
    private readonly TimeProvider _time;
    private int _loadVersion;
    private bool _active; // до первой явной загрузки (после инициализации БД) к БД не обращаемся
    private bool _settingTitle;
    private NotepadAddResult? _lastAdded;

    public NotepadViewModel(NotepadService notepad, EntryService entries, AppEvents events, IUserInteraction interaction, TimeProvider time)
    {
        _notepad = notepad;
        _entries = entries;
        _events = events;
        _interaction = interaction;
        _time = time;

        TitleSaveTask = Task.CompletedTask;
        Input = new EntryInputViewModel(
            entries,
            time,
            async (text, date) =>
            {
                await TitleSaveTask;
                return ToInputResult(await notepad.AddFromTextAsync(text, date));
            },
            async draft =>
            {
                await TitleSaveTask;
                return ToInputResult(await notepad.AddAsync(draft));
            },
            OnAddedAsync);
        Undo = new UndoToastViewModel(time);
        Title = "";
        SummaryText = NotepadFormat.Summary(null, time.LocalTimeZone);
        SummaryHint = "";
        StatusText = "";
        ArchiveTabText = "Архив (0)";
        IsEmpty = true;
        IsArchiveEmpty = true;

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

    /// <summary>Пояснение ко второй цифре плашки (F9.4); пусто, если суммы совпадают.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummaryHint))]
    public partial string SummaryHint { get; set; }

    public bool HasSummaryHint => SummaryHint.Length > 0;

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public int MaxTitleLength => NotepadSheet.MaxTitleLength;

    /// <summary>
    /// Последнее сохранение названия. Операции блокнота сначала дожидаются его: иначе, например, «Очистить всё»,
    /// нажатое прямо из поля названия, могло бы обогнать сохранение — и название досталось бы новому листу.
    /// Главное окно дожидается его перед закрытием.
    /// </summary>
    public Task TitleSaveTask { get; private set; }

    partial void OnTitleChanged(string value)
    {
        if (_active && !_settingTitle)
        {
            TitleSaveTask = SaveTitleAsync(TitleSaveTask, value);
        }
    }

    private async Task SaveTitleAsync(Task previous, string value)
    {
        await previous; // сохранения названия идут строго по очереди
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

    // ---------- Текущий лист (F8.8, F9.3) ----------

    public ObservableCollection<NotepadLineRow> Rows { get; } = [];

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
        SummaryHint = NotepadFormat.SummaryHint(current);

        Rows.Clear();
        SelectedRows.Clear();
        foreach (var line in current?.Lines ?? [])
        {
            Rows.Add(new NotepadLineRow(line));
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

    // ---------- Действия со строками (F9.2, F9.5, F9.8) ----------

    /// <summary>Ввод отдаёт результат как у «Журнала»; ошибки — только формата (F9.1), пересечение — не ошибка (F9.2).</summary>
    private EntryOperationResult ToInputResult(NotepadAddResult result)
    {
        _lastAdded = result;
        return new EntryOperationResult([.. result.Lines.Select(l => l.Values)], result.Errors);
    }

    private async Task OnAddedAsync(int count)
    {
        StatusText = NotepadFormat.Added(count, _lastAdded?.NotInStatisticsCount ?? 0);
        var entriesChanged = _lastAdded is null || _lastAdded.NotInStatisticsCount < _lastAdded.Lines.Count;
        _lastAdded = null;
        Undo.Invalidate();
        await LoadAsync();
        if (entriesChanged)
        {
            _events.RaiseEntriesChanged(this);
        }
    }

    /// <summary>
    /// Правка строки (F9.5): тот же диалог, что в «Журнале», но меняется строка блокнота; запись базы — только если
    /// новое время ни с чем не пересекается. Пересечение — не ошибка: строка меняется только в блокноте.
    /// </summary>
    [RelayCommand]
    private async Task EditEntryAsync(NotepadLineRow? row)
    {
        if (row is null)
        {
            return;
        }

        NotepadEditResult? result = null;
        var editor = new EntryEditorViewModel(
            row.Line.Values,
            _entries,
            async draft =>
            {
                await TitleSaveTask;
                result = await _notepad.UpdateLineAsync(row.Id, draft);
                return result.Errors;
            },
            "Изменить строку блокнота",
            EditHint);
        if (await _interaction.EditEntryAsync(editor) && result is { Line: { } line })
        {
            StatusText = NotepadFormat.Edited(line);
            Undo.Invalidate();
            await LoadAsync();
            if (result.EntriesChanged)
            {
                _events.RaiseEntriesChanged(this);
            }
        }
    }

    [RelayCommand]
    private Task RemoveEntryAsync(NotepadLineRow? row) => row is null ? Task.CompletedTask : RemoveAsync([row.Id]);

    [RelayCommand]
    private Task RemoveSelectedAsync() => RemoveAsync([.. SelectedRows.OfType<NotepadLineRow>().Select(r => r.Id)]);

    /// <summary>
    /// ✕ / Del: строка убирается только из блокнота; связанная запись остаётся в журнале и статистике (F9.8).
    /// Полного удаления записи на этой вкладке нет намеренно (F8.13).
    /// </summary>
    private async Task RemoveAsync(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await TitleSaveTask;
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

    /// <summary>«Очистить всё»: лист уходит в архив, записи остаются в журнале и статистике (F9.9). Без подтверждения — с отменой.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAllAsync()
    {
        await TitleSaveTask;
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

}
