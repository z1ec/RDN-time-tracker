using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Services;

/// <summary>Результат добавления строк в блокнот: строки или ошибки формата (F9.1).</summary>
public sealed record NotepadAddResult(IReadOnlyList<NotepadLine> Lines, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>Сколько добавленных строк не попало в статистику из-за пересечений (F9.2).</summary>
    public int NotInStatisticsCount => Lines.Count(l => l.Linked is null);

    public static NotepadAddResult Fail(IReadOnlyList<string> errors) => new([], errors);
}

/// <summary>Результат правки строки блокнота (F9.5).</summary>
public sealed record NotepadEditResult(NotepadLine? Line, bool EntriesChanged, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0;

    public static NotepadEditResult Fail(string error) => new(null, false, [error]);
}

/// <summary>
/// Сценарии блокнота-песочницы (F8, F9, ADR-0010). У строки блокнота свои значения; в базу (журнал и статистику)
/// время попадает, только если не пересекается с записями базы (F9.2) — проверка та же, что в «Журнале».
/// ✕, очистка и архив меняют только таблицы блокнота — записи и статистика не затрагиваются.
/// </summary>
public sealed class NotepadService(INotepadRepository notepad, EntryService entries)
{
    /// <summary>
    /// Добавляет строки из текста (F9.1, F9.2). Ошибка формата в любой строке — не добавляется ничего;
    /// пересечение — не ошибка: строка добавляется только в блокнот. Строки обрабатываются по порядку одной транзакцией.
    /// </summary>
    public async Task<NotepadAddResult> AddFromTextAsync(string? text, DateOnly selectedDate, CancellationToken ct = default)
    {
        var batch = entries.Parser.ParseMany(text, selectedDate);
        if (batch.IsEmpty)
        {
            return new NotepadAddResult([], []);
        }

        return batch.IsSuccess
            ? new NotepadAddResult(await AddDraftsAsync([.. batch.Lines.Select(l => l.Draft)], ct).ConfigureAwait(false), [])
            : NotepadAddResult.Fail(batch.ErrorMessages);
    }

    /// <summary>Добавляет строку из формы по тем же правилам.</summary>
    public async Task<NotepadAddResult> AddAsync(EntryDraft draft, CancellationToken ct = default)
    {
        var error = entries.Revalidate(draft);
        return error is null
            ? new NotepadAddResult(await AddDraftsAsync([draft], ct).ConfigureAwait(false), [])
            : NotepadAddResult.Fail([error]);
    }

    /// <summary>
    /// Правка строки (F9.5): строка меняется всегда. Связанная строка — то же изменение пробуем применить к записи
    /// (пересечения без учёта самой записи); не вышло — запись не меняется, статус «в статистике другое время».
    /// Несвязанная строка без пересечений — создаётся запись и строка связывается (правило F9.2).
    /// </summary>
    public async Task<NotepadEditResult> UpdateLineAsync(long lineId, EntryDraft draft, CancellationToken ct = default)
    {
        if (entries.Revalidate(draft) is { } error)
        {
            return NotepadEditResult.Fail(error);
        }

        var line = await notepad.GetLineAsync(lineId, ct).ConfigureAwait(false);
        if (line is null)
        {
            return NotepadEditResult.Fail("Строка не найдена — возможно, она уже убрана из блокнота");
        }

        var conflict = (await entries.FindConflictsAsync([(draft, line.EntryId)], accumulate: false, ct).ConfigureAwait(false))[0];
        var mode = conflict is not null ? NotepadLineUpdate.LineOnly
            : line.Linked is null ? NotepadLineUpdate.LineAndNewEntry
            : NotepadLineUpdate.LineAndEntry;
        var updated = await notepad.UpdateLineAsync(lineId, draft, mode, ct).ConfigureAwait(false);
        return updated is null
            ? NotepadEditResult.Fail("Строка не найдена — возможно, она уже убрана из блокнота")
            : new NotepadEditResult(updated with { Conflict = conflict }, mode != NotepadLineUpdate.LineOnly, []);
    }

    /// <summary>Текущий лист; у строк не «в статистике» найден конфликт на момент показа (F9.3).</summary>
    public async Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default)
    {
        var current = await notepad.GetCurrentAsync(ct).ConfigureAwait(false);
        return current is null ? null : (await WithConflictsAsync([current], ct).ConfigureAwait(false))[0];
    }

    /// <summary>Архив; статусы и подсказки — по тем же правилам, что у текущего листа (F9.9).</summary>
    public async Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default) =>
        await WithConflictsAsync(await notepad.GetArchiveAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>Сохраняет название листа (F8.7): пробелы по краям обрезаются, длина — до 100 символов. Возвращает сохранённое значение.</summary>
    public async Task<string> SetTitleAsync(string? title, CancellationToken ct = default)
    {
        var normalized = NormalizeTitle(title);
        await notepad.SetTitleAsync(normalized, ct).ConfigureAwait(false);
        return normalized;
    }

    /// <summary>Убирает строки из блокнота (✕, F9.8). Связанные записи остаются в журнале и статистике.</summary>
    public Task<int> RemoveAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default) =>
        lineIds.Count == 0 ? Task.FromResult(0) : notepad.RemoveLinesAsync(lineIds, ct);

    public Task<int> UndoRemoveAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default) =>
        lineIds.Count == 0 ? Task.FromResult(0) : notepad.RestoreLinesAsync(lineIds, ct);

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

    /// <summary>
    /// F9.2: по порядку решает, писать ли строку в базу — без пересечений с записями базы и с уже решёнными
    /// «писать» строками этой же вставки. Сохранение — одна транзакция.
    /// </summary>
    private async Task<IReadOnlyList<NotepadLine>> AddDraftsAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct)
    {
        var conflicts = await entries.FindConflictsAsync([.. drafts.Select(d => (d, (long?)null))], accumulate: true, ct).ConfigureAwait(false);
        var added = await notepad.AddLinesAsync([.. drafts.Select((d, i) => new NotepadLineDraft(d, conflicts[i] is null))], ct).ConfigureAwait(false);
        return [.. added.Select((l, i) => l with { Conflict = conflicts[i] })];
    }

    /// <summary>Для строк не «в статистике» — запись, с которой строка пересекается сейчас (подсказка F9.3).</summary>
    private async Task<IReadOnlyList<NotepadSheetView>> WithConflictsAsync(IReadOnlyList<NotepadSheetView> sheets, CancellationToken ct)
    {
        var pending = sheets.SelectMany(s => s.Lines).Where(l => l.Status != NotepadLineStatus.InStatistics).ToList();
        if (pending.Count == 0)
        {
            return sheets;
        }

        var found = await entries.FindConflictsAsync([.. pending.Select(l => (l.ToDraft(), l.EntryId))], accumulate: false, ct).ConfigureAwait(false);
        var byLine = pending.Select((l, i) => (l.Id, Conflict: found[i])).ToDictionary(x => x.Id, x => x.Conflict);
        return [.. sheets.Select(s => s with { Lines = [.. s.Lines.Select(l => byLine.TryGetValue(l.Id, out var c) ? l with { Conflict = c } : l)] })];
    }
}
