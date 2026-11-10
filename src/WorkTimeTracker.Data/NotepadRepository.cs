using Dapper;
using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data;

/// <summary>
/// Хранилище блокнота в SQLite (F8, миграция 0003). Строки <c>entries</c> здесь только добавляются
/// (<see cref="AddEntriesAsync"/>) и читаются — не изменяются и не удаляются (ADR-0009).
/// Видимые записи листа: привязка не убрана (<c>removed_at IS NULL</c>) и запись не удалена (<c>deleted_at IS NULL</c>).
/// </summary>
public sealed class NotepadRepository(SqliteConnectionFactory factory, TimeProvider time) : INotepadRepository
{
    private const string CurrentFilter = "cleared_at IS NULL AND deleted_at IS NULL";
    private const string SheetColumns = "id AS Id, title AS Title, started_at AS StartedAt, cleared_at AS ClearedAt";

    public Task<IReadOnlyList<WorkEntry>> AddEntriesAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default) =>
        InTransaction<IReadOnlyList<WorkEntry>>(
            (c, tx, now) =>
            {
                var sheetId = FindCurrentId(c, tx) ?? CreateSheet(c, tx, string.Empty, now);
                var added = EntryRepository.InsertEntries(c, tx, drafts, now);
                foreach (var entry in added)
                {
                    c.Execute(
                        "INSERT INTO notepad_items (entry_id, sheet_id, added_at) VALUES (@entryId, @sheetId, @now)",
                        new { entryId = entry.Id, sheetId, now },
                        tx);
                }

                return added;
            },
            ct);

    public Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                var sheet = c.QuerySingleOrDefault<SheetRow>($"SELECT {SheetColumns} FROM notepad_sheets WHERE {CurrentFilter}");
                return sheet is null ? null : new NotepadSheetView(sheet.ToSheet(), LoadEntries(c, [sheet.Id])[sheet.Id].ToList());
            },
            ct);

    public Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default) =>
        factory.RunAsync<IReadOnlyList<NotepadSheetView>>(
            c =>
            {
                var sheets = c.Query<SheetRow>(
                    $"SELECT {SheetColumns} FROM notepad_sheets WHERE cleared_at IS NOT NULL AND deleted_at IS NULL ORDER BY cleared_at DESC, id DESC").ToList();
                var entries = LoadEntries(c, [.. sheets.Select(s => s.Id)]);
                return [.. sheets.Select(s => new NotepadSheetView(s.ToSheet(), entries[s.Id].ToList()))];
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

    public Task<int> RemoveItemsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, now) => c.Execute(
                $"""
                 UPDATE notepad_items SET removed_at = @now
                 WHERE entry_id IN @entryIds AND removed_at IS NULL
                   AND sheet_id IN (SELECT id FROM notepad_sheets WHERE {CurrentFilter})
                 """,
                new { entryIds, now },
                tx),
            ct);

    public Task<int> RestoreItemsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default) =>
        InTransaction(
            (c, tx, _) => c.Execute(
                "UPDATE notepad_items SET removed_at = NULL WHERE entry_id IN @entryIds AND removed_at IS NOT NULL", new { entryIds }, tx),
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
                var items = c.Execute("DELETE FROM notepad_items WHERE removed_at IS NOT NULL AND removed_at < @threshold", new { threshold }, tx);

                // Привязки удалённого листа уходят каскадом; записи entries не затрагиваются.
                var sheets = c.Execute("DELETE FROM notepad_sheets WHERE deleted_at IS NOT NULL AND deleted_at < @threshold", new { threshold }, tx);
                return new NotepadPurgeResult(items, sheets);
            },
            ct);

    private static long? FindCurrentId(SqliteConnection c, SqliteTransaction tx) =>
        c.ExecuteScalar<long?>($"SELECT id FROM notepad_sheets WHERE {CurrentFilter}", transaction: tx);

    private static long CreateSheet(SqliteConnection c, SqliteTransaction tx, string title, string now) =>
        c.ExecuteScalar<long>(
            "INSERT INTO notepad_sheets (title, started_at) VALUES (@title, @now); SELECT last_insert_rowid();", new { title, now }, tx);

    /// <summary>Видимые записи листов, по возрастанию даты и начала (F8.8).</summary>
    private static ILookup<long, WorkEntry> LoadEntries(SqliteConnection c, IReadOnlyCollection<long> sheetIds) =>
        c.Query<ItemRow>(
                """
                SELECT i.sheet_id AS SheetId, e.id AS Id, e.work_date AS WorkDate, e.start_min AS StartMin, e.end_min AS EndMin, e.comment AS Comment
                FROM notepad_items i JOIN entries e ON e.id = i.entry_id
                WHERE i.sheet_id IN @sheetIds AND i.removed_at IS NULL AND e.deleted_at IS NULL
                ORDER BY e.work_date, e.start_min
                """,
                new { sheetIds })
            .ToLookup(r => r.SheetId, r => r.ToEntry());

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

    private sealed class ItemRow
    {
        public long SheetId { get; init; }

        public long Id { get; init; }

        public string WorkDate { get; init; } = "";

        public long StartMin { get; init; }

        public long EndMin { get; init; }

        public string Comment { get; init; } = "";

        public WorkEntry ToEntry() => new(Id, EntryRepository.ParseDate(WorkDate), (int)StartMin, (int)EndMin, Comment);
    }
}
