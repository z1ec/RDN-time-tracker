using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Abstractions;

/// <summary>
/// Хранилище блокнота (F8, F9; таблицы <c>notepad_sheets</c> и <c>notepad_lines</c>).
/// Строки <c>entries</c> здесь только создаются и изменяются вместе со строкой блокнота (<see cref="AddLinesAsync"/>,
/// <see cref="UpdateLineAsync"/>) — и никогда не удаляются. ✕, очистка и архив записей не касаются.
/// Каждый метод изменения — одна транзакция.
/// </summary>
public interface INotepadRepository
{
    /// <summary>
    /// Добавляет строки в текущий лист по порядку одной транзакцией (F9.2). Для строк с
    /// <see cref="NotepadLineDraft.CreateEntry"/> в той же транзакции создаётся запись журнала и строка связывается с ней.
    /// Текущий лист создаётся, если его ещё нет.
    /// </summary>
    Task<IReadOnlyList<NotepadLine>> AddLinesAsync(IReadOnlyList<NotepadLineDraft> lines, CancellationToken ct = default);

    /// <summary>Видимая строка текущего листа или <c>null</c> (нет такой, убрана, в архиве, её запись удалена).</summary>
    Task<NotepadLine?> GetLineAsync(long lineId, CancellationToken ct = default);

    /// <summary>
    /// Меняет видимую строку текущего листа одной транзакцией (F9.5); <paramref name="mode"/> — что сделать с записью базы.
    /// Возвращает строку после изменения или <c>null</c>, если строки нет.
    /// </summary>
    Task<NotepadLine?> UpdateLineAsync(long lineId, EntryDraft values, NotepadLineUpdate mode, CancellationToken ct = default);

    /// <summary>Текущий лист с видимыми строками или <c>null</c>, если он ещё не создан.</summary>
    Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Архив: очищенные и не удалённые листы, новые сверху (F8.15).</summary>
    Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default);

    /// <summary>Задаёт название текущего листа. Если листа нет, он создаётся — но только для непустого названия.</summary>
    Task SetTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Убирает строки текущего листа (✕, F9.8); записи журнала не трогает. Возвращает число убранных.</summary>
    Task<int> RemoveLinesAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default);

    /// <summary>Отмена <see cref="RemoveLinesAsync"/>. Возвращает число возвращённых строк.</summary>
    Task<int> RestoreLinesAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default);

    /// <summary>«Очистить всё» (F8.14): текущий лист уходит в архив. Возвращает его id или <c>null</c>, если листа нет.</summary>
    Task<long?> ClearCurrentAsync(CancellationToken ct = default);

    /// <summary>Отмена очистки: возможна, только пока не создан новый текущий лист.</summary>
    Task<bool> UndoClearAsync(long sheetId, CancellationToken ct = default);

    /// <summary>«Удалить из архива» (F8.17): мягко удаляет архивный лист, записи остаются.</summary>
    Task<bool> DeleteSheetAsync(long sheetId, CancellationToken ct = default);

    /// <summary>Отмена <see cref="DeleteSheetAsync"/>.</summary>
    Task<bool> RestoreSheetAsync(long sheetId, CancellationToken ct = default);

    /// <summary>Физически удаляет строки, убранные раньше порога, и листы, удалённые из архива раньше порога (F8.18).</summary>
    Task<NotepadPurgeResult> PurgeAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default);
}
