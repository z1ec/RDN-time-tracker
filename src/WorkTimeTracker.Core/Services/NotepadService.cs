using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Services;

/// <summary>
/// Сценарии блокнота (F8). Добавление идёт через <see cref="EntryService"/> — тот же разбор, те же правила
/// и проверка пересечений со всеми записями (F8.2, F8.3); отличается только способ сохранения:
/// запись и привязка к листу создаются одной транзакцией (F8.4).
/// Остальные операции меняют только листы и привязки — записи и статистика не затрагиваются (ADR-0009).
/// </summary>
public sealed class NotepadService(INotepadRepository notepad, EntryService entries)
{
    /// <summary>Добавляет записи из текста в журнал и в текущий лист. Атомарно, как в журнале (F1.2).</summary>
    public Task<EntryOperationResult> AddFromTextAsync(string? text, DateOnly selectedDate, CancellationToken ct = default) =>
        entries.AddFromTextAsync(text, selectedDate, notepad.AddEntriesAsync, ct);

    /// <summary>Добавляет запись из формы в журнал и в текущий лист.</summary>
    public Task<EntryOperationResult> AddAsync(EntryDraft draft, CancellationToken ct = default) =>
        entries.AddAsync(draft, notepad.AddEntriesAsync, ct);

    public Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default) => notepad.GetCurrentAsync(ct);

    public Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default) => notepad.GetArchiveAsync(ct);

    /// <summary>Сохраняет название листа (F8.7): пробелы по краям обрезаются, длина — до 100 символов. Возвращает сохранённое значение.</summary>
    public async Task<string> SetTitleAsync(string? title, CancellationToken ct = default)
    {
        var normalized = NormalizeTitle(title);
        await notepad.SetTitleAsync(normalized, ct).ConfigureAwait(false);
        return normalized;
    }

    /// <summary>Убирает записи из блокнота (F8.11). Записи остаются в журнале и статистике.</summary>
    public Task<int> RemoveAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default) =>
        entryIds.Count == 0 ? Task.FromResult(0) : notepad.RemoveItemsAsync(entryIds, ct);

    public Task<int> UndoRemoveAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default) =>
        entryIds.Count == 0 ? Task.FromResult(0) : notepad.RestoreItemsAsync(entryIds, ct);

    /// <summary>
    /// «Очистить всё» (F8.14): лист уходит в архив. Пустой блокнот не очищается — возвращается <c>null</c>.
    /// </summary>
    public async Task<long?> ClearAsync(CancellationToken ct = default)
    {
        var current = await notepad.GetCurrentAsync(ct).ConfigureAwait(false);
        return current is null || current.IsEmpty ? null : await notepad.ClearCurrentAsync(ct).ConfigureAwait(false);
    }

    public Task<bool> UndoClearAsync(long sheetId, CancellationToken ct = default) => notepad.UndoClearAsync(sheetId, ct);

    /// <summary>«Удалить из архива» (F8.17): удаляется только лист.</summary>
    public Task<bool> DeleteSheetAsync(long sheetId, CancellationToken ct = default) => notepad.DeleteSheetAsync(sheetId, ct);

    public Task<bool> UndoDeleteSheetAsync(long sheetId, CancellationToken ct = default) => notepad.RestoreSheetAsync(sheetId, ct);

    public static string NormalizeTitle(string? title)
    {
        var t = (title ?? string.Empty).Trim();
        return t.Length <= NotepadSheet.MaxTitleLength ? t : t[..NotepadSheet.MaxTitleLength].TrimEnd();
    }
}
