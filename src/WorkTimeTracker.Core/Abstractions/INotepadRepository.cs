using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Abstractions;

/// <summary>
/// Хранилище блокнота (F8, таблицы <c>notepad_sheets</c> и <c>notepad_items</c>).
/// Ни один метод не изменяет и не удаляет существующие строки <c>entries</c>;
/// <see cref="AddEntriesAsync"/> только добавляет новые. Каждый метод изменения — одна транзакция.
/// </summary>
public interface INotepadRepository
{
    /// <summary>
    /// Создаёт записи и привязывает их к текущему листу одной транзакцией (F8.4).
    /// Текущий лист создаётся, если его ещё нет.
    /// </summary>
    Task<IReadOnlyList<WorkEntry>> AddEntriesAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default);

    /// <summary>Текущий лист с видимыми записями или <c>null</c>, если он ещё не создан.</summary>
    Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Архив: очищенные и не удалённые листы, новые сверху (F8.15).</summary>
    Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default);

    /// <summary>Задаёт название текущего листа. Если листа нет, он создаётся — но только для непустого названия.</summary>
    Task SetTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Убирает записи из текущего листа (✕, F8.11); сами записи не трогает. Возвращает число убранных.</summary>
    Task<int> RemoveItemsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default);

    /// <summary>Отмена <see cref="RemoveItemsAsync"/>. Возвращает число возвращённых привязок.</summary>
    Task<int> RestoreItemsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default);

    /// <summary>«Очистить всё» (F8.14): текущий лист уходит в архив. Возвращает его id или <c>null</c>, если листа нет.</summary>
    Task<long?> ClearCurrentAsync(CancellationToken ct = default);

    /// <summary>Отмена очистки: возможна, только пока не создан новый текущий лист.</summary>
    Task<bool> UndoClearAsync(long sheetId, CancellationToken ct = default);

    /// <summary>«Удалить из архива» (F8.17): мягко удаляет архивный лист, записи остаются.</summary>
    Task<bool> DeleteSheetAsync(long sheetId, CancellationToken ct = default);

    /// <summary>Отмена <see cref="DeleteSheetAsync"/>.</summary>
    Task<bool> RestoreSheetAsync(long sheetId, CancellationToken ct = default);

    /// <summary>Физически удаляет привязки, убранные раньше порога, и листы, удалённые из архива раньше порога (F8.18).</summary>
    Task<NotepadPurgeResult> PurgeAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default);
}
