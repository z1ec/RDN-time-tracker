using Dapper;
using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data;

/// <summary>
/// Хранилище блокнота в SQLite (F8, F9; миграции 0003, 0004). У строки блокнота свои дата, время и комментарий,
/// <c>entry_id</c> связывает её с записью журнала (ADR-0010). Строки <c>entries</c> здесь только создаются и изменяются
/// вместе со строкой (<see cref="AddLinesAsync"/>, <see cref="UpdateLineAsync"/>) — и никогда не удаляются.
/// Видимые строки листа: <c>removed_at IS NULL</c> и связанная запись (если есть) не удалена.
/// </summary>
public sealed class NotepadRepository(SqliteConnectionFactory factory, TimeProvider time) : INotepadRepository
{
    private const string CurrentFilter = "cleared_at IS NULL AND deleted_at IS NULL";
    private const string SheetColumns = "id AS Id, title AS Title, started_at AS StartedAt, cleared_at AS ClearedAt";

    /// <summary>Строки с текущими значениями связанных записей; видимость — <see cref="VisibleFilter"/>.</summary>
    private const string LineSelect =
        """
        SELECT l.id AS Id, l.sheet_id AS SheetId, l.work_date AS WorkDate, l.start_min AS StartMin, l.end_min AS EndMin, l.comment AS Comment,
               e.id AS EntryId, e.work_date AS EntryWorkDate, e.start_min AS EntryStartMin, e.end_min AS EntryEndMin, e.comment AS EntryComment
        FROM notepad_lines l LEFT JOIN entries e ON e.id = l.entry_id
        """;

    /// <summary>Видимость строки (F9.7, F9.8): не убрана ✕ и не связана с удалённой в «Журнале» записью.</summary>
    private const string VisibleFilter = "l.removed_at IS NULL AND (l.entry_id IS NULL OR e.deleted_at IS NULL)";

    public Task<IReadOnlyList<NotepadLine>> AddLinesAsync(IReadOnlyList<NotepadLineDraft> lines, CancellationToken ct = default) =>
        InTransaction<IReadOnlyList<NotepadLine>>(
            (c, tx, now) =>
            {
                var sheetId = FindCurrentId(c, tx) ?? CreateSheet(c, tx, string.Empty, now);
                var added = new List<NotepadLine>(lines.Count);
                foreach (var line in lines)
                {
                    var entry = line.CreateEntry ? EntryRepository.InsertEntries(c, tx, [line.Draft], now)[0] : null;
                    var id = c.ExecuteScalar<long>(
                        """
                        INSERT INTO notepad_lines (sheet_id, work_date, start_min, end_min, comment, entry_id, added_at, updated_at)
                        VALUES (@sheetId, @date, @start, @end, @comment, @entryId, @now, @now);
                        SELECT last_insert_rowid();
                        """,
                        new { sheetId, entryId = entry?.Id, now, date = EntryRepository.FormatDate(line.Draft.Date), start = line.Draft.Range.StartMinute, end = line.Draft.Range.EndMinute, comment = line.Draft.Comment },
                        tx);
                    added.Add(new NotepadLine(id, line.Draft.Date, line.Draft.Range.StartMinute, line.Draft.Range.EndMinute, line.Draft.Comment, entry));
                }

                return added;
            },
            ct);

    public Task<NotepadLine?> GetLineAsync(long lineId, CancellationToken ct = default) =>
        factory.RunAsync(c => FindCurrentLine(c, null, lineId), ct);

    public Task<NotepadLine?> UpdateLineAsync(long lineId, EntryDraft values, NotepadLineUpdate mode, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, now) =>
            {
                var line = FindCurrentLine(c, tx, lineId);
                if (line is null)
                {
                    return null;
                }

                var args = new { lineId, now, date = EntryRepository.FormatDate(values.Date), start = values.Range.StartMinute, end = values.Range.EndMinute, comment = values.Comment };
                c.Execute(
                    "UPDATE notepad_lines SET work_date = @date, start_min = @start, end_min = @end, comment = @comment, updated_at = @now WHERE id = @lineId",
                    args,
                    tx);

                if (mode == NotepadLineUpdate.LineAndEntry && line.EntryId is { } entryId)
                {
                    EntryRepository.UpdateEntry(c, tx, values.ToEntry(entryId), now);
                }
                else if (mode == NotepadLineUpdate.LineAndNewEntry && line.EntryId is null)
                {
                    var entry = EntryRepository.InsertEntries(c, tx, [values], now)[0];
                    c.Execute("UPDATE notepad_lines SET entry_id = @entryId WHERE id = @lineId", new { lineId, entryId = entry.Id }, tx);
                }

                return FindCurrentLine(c, tx, lineId);
            },
            ct);

    public Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                var sheet = c.QuerySingleOrDefault<SheetRow>($"SELECT {SheetColumns} FROM notepad_sheets WHERE {CurrentFilter}");
                return sheet is null ? null : new NotepadSheetView(sheet.ToSheet(), LoadLines(c, [sheet.Id])[sheet.Id].ToList());
            },
            ct);

    public Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default) =>
        factory.RunAsync<IReadOnlyList<NotepadSheetView>>(
            c =>
            {
                var sheets = c.Query<SheetRow>(
                    $"SELECT {SheetColumns} FROM notepad_sheets WHERE cleared_at IS NOT NULL AND deleted_at IS NULL ORDER BY cleared_at DESC, id DESC").ToList();
                var lines = LoadLines(c, [.. sheets.Select(s => s.Id)]);
                return [.. sheets.Select(s => new NotepadSheetView(s.ToSheet(), lines[s.Id].ToList()))];
            },
            ct);

    public Task SetTitleAsync(string title, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, now) =>
            {
                if (FindCurrentId(c, tx) is { } id)
                {
                    c.Execute("UPDATE notepad_sheets SET title = @title WHERE id = @id", new { id, title }, tx);
                }
                else if (title.Length > 0)
                {
                    CreateSheet(c, tx, title, now);
                }

                return true;
            },
            ct);

    public Task<int> RemoveLinesAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, now) => c.Execute(
                $"""
                 UPDATE notepad_lines SET removed_at = @now
                 WHERE id IN @lineIds AND removed_at IS NULL
                   AND sheet_id IN (SELECT id FROM notepad_sheets WHERE {CurrentFilter})
                 """,
                new { lineIds, now },
                tx),
            ct);

    public Task<int> RestoreLinesAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, _) => c.Execute(
                "UPDATE notepad_lines SET removed_at = NULL WHERE id IN @lineIds AND removed_at IS NOT NULL", new { lineIds }, tx),
            ct);

    public Task<long?> ClearCurrentAsync(CancellationToken ct = default) =>
        InTransaction(
            (c, tx, now) =>
            {
                var id = FindCurrentId(c, tx);
                if (id is not null)
                {
                    c.Execute("UPDATE notepad_sheets SET cleared_at = @now WHERE id = @id", new { id, now }, tx);
                }

                return id;
            },
            ct);

    public Task<bool> UndoClearAsync(long sheetId, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, _) =>

                // Новый текущий лист уже создан — вернуть старый нельзя: текущий лист всегда один.
                FindCurrentId(c, tx) is null
                && c.Execute(
                    "UPDATE notepad_sheets SET cleared_at = NULL WHERE id = @sheetId AND cleared_at IS NOT NULL AND deleted_at IS NULL",
                    new { sheetId },
                    tx) == 1,
            ct);

    public Task<bool> DeleteSheetAsync(long sheetId, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, now) => c.Execute(
                "UPDATE notepad_sheets SET deleted_at = @now WHERE id = @sheetId AND cleared_at IS NOT NULL AND deleted_at IS NULL",
                new { sheetId, now },
                tx) == 1,
            ct);

    public Task<bool> RestoreSheetAsync(long sheetId, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, _) => c.Execute(
                "UPDATE notepad_sheets SET deleted_at = NULL WHERE id = @sheetId AND cleared_at IS NOT NULL AND deleted_at IS NOT NULL",
                new { sheetId },
                tx) == 1,
            ct);

    public Task<NotepadPurgeResult> PurgeAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, _) =>
            {
                var threshold = EntryRepository.FormatTimestamp(olderThanUtc);
                var lines = c.Execute("DELETE FROM notepad_lines WHERE removed_at IS NOT NULL AND removed_at < @threshold", new { threshold }, tx);

                // Строки удалённого листа уходят каскадом; записи entries не затрагиваются.
                var sheets = c.Execute("DELETE FROM notepad_sheets WHERE deleted_at IS NOT NULL AND deleted_at < @threshold", new { threshold }, tx);
                return new NotepadPurgeResult(lines, sheets);
            },
            ct);

    private static long? FindCurrentId(SqliteConnection c, SqliteTransaction tx) =>
        c.ExecuteScalar<long?>($"SELECT id FROM notepad_sheets WHERE {CurrentFilter}", transaction: tx);

    private static long CreateSheet(SqliteConnection c, SqliteTransaction tx, string title, string now) =>
        c.ExecuteScalar<long>(
            "INSERT INTO notepad_sheets (title, started_at) VALUES (@title, @now); SELECT last_insert_rowid();", new { title, now }, tx);

    /// <summary>Видимая строка текущего листа: править и убирать можно только их (архив — только для чтения).</summary>
    private static NotepadLine? FindCurrentLine(SqliteConnection c, SqliteTransaction? tx, long lineId) =>
        c.QuerySingleOrDefault<LineRow>(
            $"{LineSelect} WHERE l.id = @lineId AND {VisibleFilter} AND l.sheet_id IN (SELECT id FROM notepad_sheets WHERE {CurrentFilter})",
            new { lineId },
            tx)?.ToLine();

    /// <summary>Видимые строки листов, по возрастанию даты и начала (F8.8).</summary>
    private static ILookup<long, NotepadLine> LoadLines(SqliteConnection c, IReadOnlyCollection<long> sheetIds) =>
        c.Query<LineRow>(
                $"{LineSelect} WHERE l.sheet_id IN @sheetIds AND {VisibleFilter} ORDER BY l.work_date, l.start_min, l.id",
                new { sheetIds })
            .ToLookup(r => r.SheetId, r => r.ToLine());

    private Task<T> InTransaction<T>(Func<SqliteConnection, SqliteTransaction, string, T> work, CancellationToken ct) =>
        factory.RunAsync(
            c =>
            {
                // Блокировка на запись берётся сразу: проверка «есть ли текущий лист» и вставка не разрываются.
                using var tx = c.BeginTransaction(deferred: false);
                var result = work(c, tx, EntryRepository.FormatTimestamp(time.GetUtcNow()));
                tx.Commit();
                return result;
            },
            ct);

    private sealed class SheetRow
    {
        public long Id { get; init; }

        public string Title { get; init; } = "";

        public string StartedAt { get; init; } = "";

        public string? ClearedAt { get; init; }

        public NotepadSheet ToSheet() =>
            new(Id, Title, EntryRepository.ParseTimestamp(StartedAt), ClearedAt is null ? null : EntryRepository.ParseTimestamp(ClearedAt));
    }

    private sealed class LineRow
    {
        public long Id { get; init; }

        public long SheetId { get; init; }

        public string WorkDate { get; init; } = "";

        public long StartMin { get; init; }

        public long EndMin { get; init; }

        public string Comment { get; init; } = "";

        public long? EntryId { get; init; }

        public string? EntryWorkDate { get; init; }

        public long? EntryStartMin { get; init; }

        public long? EntryEndMin { get; init; }

        public string? EntryComment { get; init; }

        public NotepadLine ToLine()
        {
            var linked = EntryId is { } id
                ? new WorkEntry(id, EntryRepository.ParseDate(EntryWorkDate!), (int)EntryStartMin!.Value, (int)EntryEndMin!.Value, EntryComment ?? "")
                : null;
            return new NotepadLine(Id, EntryRepository.ParseDate(WorkDate), (int)StartMin, (int)EndMin, Comment, linked);
        }
    }
}
