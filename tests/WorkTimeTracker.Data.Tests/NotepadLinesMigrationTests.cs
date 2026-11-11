using Dapper;
using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>
/// S27 (F9, миграция 0004): обновление настоящей БД версии 1.1.0 (схема v3, <c>notepad_items</c>) до схемы v4
/// (<c>notepad_lines</c>). Фикстура — замороженная копия схемы 1.1.0 и данные, как их записывала версия 1.1.0:
/// текущий лист с названием, архив, удалённый из архива лист, убранные ✕ привязки и удалённые в «Журнале» записи.
/// </summary>
public class NotepadLinesMigrationTests
{
    /// <summary>Схема WorkTime Tracker 1.1.0 (миграции 0001–0003) — намеренно продублирована, а не взята из сборки.</summary>
    private const string SchemaV110 =
        """
        CREATE TABLE schema_version (version INTEGER NOT NULL, applied_at TEXT NOT NULL);
        CREATE TABLE entries (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            work_date   TEXT    NOT NULL,
            start_min   INTEGER NOT NULL CHECK (start_min BETWEEN 0 AND 1439),
            end_min     INTEGER NOT NULL,
            comment     TEXT    NOT NULL DEFAULT '',
            created_at  TEXT    NOT NULL,
            updated_at  TEXT    NOT NULL,
            deleted_at  TEXT    NULL,
            CHECK (end_min > start_min AND end_min - start_min <= 1440),
            CHECK (length(comment) <= 500)
        );
        CREATE INDEX ix_entries_work_date ON entries(work_date) WHERE deleted_at IS NULL;
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE INDEX ix_entries_deleted_at ON entries(deleted_at) WHERE deleted_at IS NOT NULL;
        CREATE TABLE notepad_sheets (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            title       TEXT    NOT NULL DEFAULT '' CHECK (length(title) <= 100),
            started_at  TEXT    NOT NULL,
            cleared_at  TEXT    NULL,
            deleted_at  TEXT    NULL
        );
        CREATE UNIQUE INDEX ux_notepad_sheets_current ON notepad_sheets((1)) WHERE cleared_at IS NULL AND deleted_at IS NULL;
        CREATE TABLE notepad_items (
            entry_id    INTEGER PRIMARY KEY REFERENCES entries(id) ON DELETE CASCADE,
            sheet_id    INTEGER NOT NULL REFERENCES notepad_sheets(id) ON DELETE CASCADE,
            added_at    TEXT    NOT NULL,
            removed_at  TEXT    NULL
        );
        CREATE INDEX ix_notepad_items_sheet ON notepad_items(sheet_id);
        INSERT INTO schema_version (version, applied_at) VALUES
          (1, '2026-11-09T08:00:00.0000000+00:00'), (2, '2026-11-09T08:00:00.0000000+00:00'), (3, '2026-11-10T08:00:00.0000000+00:00');
        """;

    /// <summary>
    /// Лист 1 — в архиве (2 видимые записи, одна убрана ✕, одна удалена в «Журнале»), лист 2 — удалён из архива,
    /// лист 3 — текущий «Заказ: сайт кафе» (ночная смена, сутки, убранная, удалённая, комментарий с кавычками),
    /// плюс записи журнала без привязки.
    /// </summary>
    private const string DataV110 =
        """
        INSERT INTO entries (id, work_date, start_min, end_min, comment, created_at, updated_at, deleted_at) VALUES
          (1,  '2026-11-02', 540, 1080, 'макет',                 '2026-11-02T15:00:00.000Z', '2026-11-02T15:00:00.000Z', NULL),
          (2,  '2026-11-03', 540, 1005, 'вёрстка',               '2026-11-03T15:00:00.000Z', '2026-11-05T10:00:00.000Z', NULL),
          (3,  '2026-11-03', 1020, 1080, 'убрана из архивного',  '2026-11-03T18:00:00.000Z', '2026-11-03T18:00:00.000Z', NULL),
          (4,  '2026-11-04', 600, 660, 'удалена в журнале',      '2026-11-04T11:00:00.000Z', '2026-11-08T10:00:00.000Z', '2026-11-08T10:00:00.000Z'),
          (5,  '2026-11-05', 540, 600, 'лист удалён из архива',  '2026-11-05T10:00:00.000Z', '2026-11-05T10:00:00.000Z', NULL),
          (6,  '2026-11-08', 1320, 1560, 'ночная смена',         '2026-11-09T02:05:00.000Z', '2026-11-09T02:05:00.000Z', NULL),
          (7,  '2026-11-09', 0, 1440, 'дежурство, сутки',        '2026-11-10T00:01:00.000Z', '2026-11-10T00:01:00.000Z', NULL),
          (8,  '2026-11-10', 540, 600, 'убрана ✕',               '2026-11-10T10:00:00.000Z', '2026-11-10T10:00:00.000Z', NULL),
          (9,  '2026-11-10', 660, 720, 'удалена, можно отменить','2026-11-10T11:00:00.000Z', '2026-11-10T12:00:00.000Z', '2026-11-10T12:00:00.000Z'),
          (10, '2026-11-10', 780, 900, 'Созвон: «кавычки»; запятая, перенос' || char(10) || 'вторая строка', '2026-11-10T13:00:00.000Z', '2026-11-10T13:30:00.000Z', NULL),
          (11, '2026-11-10', 960, 1020, 'только журнал',         '2026-11-10T16:00:00.000Z', '2026-11-10T16:00:00.000Z', NULL),
          (12, '2026-11-06', 540, 600, 'журнал, удалена',        '2026-11-06T10:00:00.000Z', '2026-11-07T10:00:00.000Z', '2026-11-07T10:00:00.000Z');
        INSERT INTO notepad_sheets (id, title, started_at, cleared_at, deleted_at) VALUES
          (1, '',                 '2026-11-02T08:00:00.000Z', '2026-11-04T18:40:00.000Z', NULL),
          (2, 'Удалённый лист',   '2026-11-05T08:00:00.000Z', '2026-11-05T18:00:00.000Z', '2026-11-06T09:00:00.000Z'),
          (3, 'Заказ: сайт кафе', '2026-11-08T21:00:00.000Z', NULL, NULL);
        INSERT INTO notepad_items (entry_id, sheet_id, added_at, removed_at) VALUES
          (1, 1, '2026-11-02T15:00:00.000Z', NULL),
          (2, 1, '2026-11-03T15:00:00.000Z', NULL),
          (3, 1, '2026-11-03T18:00:00.000Z', '2026-11-04T09:00:00.000Z'),
          (4, 1, '2026-11-04T11:00:00.000Z', NULL),
          (5, 2, '2026-11-05T10:00:00.000Z', NULL),
          (6, 3, '2026-11-09T02:05:00.000Z', NULL),
          (7, 3, '2026-11-10T00:01:00.000Z', NULL),
          (8, 3, '2026-11-10T10:00:00.000Z', '2026-11-10T10:05:00.000Z'),
          (9, 3, '2026-11-10T11:00:00.000Z', NULL),
          (10, 3, '2026-11-10T13:00:00.000Z', NULL);
        INSERT INTO settings (key, value) VALUES ('daily_norm_minutes', '450'), ('theme', 'Dark');
        """;

    /// <summary>Видимые записи листов так, как их показывала версия 1.1.0 (запрос <c>NotepadRepository.LoadEntries</c> 1.1.0).</summary>
    private const string VisibleV110 =
        """
        SELECT i.sheet_id || '|' || e.id || '|' || e.work_date || '|' || e.start_min || '|' || e.end_min || '|' || e.comment
        FROM notepad_items i JOIN entries e ON e.id = i.entry_id
        JOIN notepad_sheets s ON s.id = i.sheet_id
        WHERE i.removed_at IS NULL AND e.deleted_at IS NULL AND s.deleted_at IS NULL
        ORDER BY i.sheet_id, e.work_date, e.start_min
        """;

    private const string AllLinksV110 =
        "SELECT entry_id || '|' || sheet_id || '|' || added_at || '|' || coalesce(removed_at, '') FROM notepad_items ORDER BY entry_id";

    private const string AllLinksV120 =
        "SELECT entry_id || '|' || sheet_id || '|' || added_at || '|' || coalesce(removed_at, '') FROM notepad_lines ORDER BY entry_id";

    private const string EntriesDump =
        "SELECT id || '|' || work_date || '|' || start_min || '|' || end_min || '|' || comment || '|' || created_at || '|' || updated_at || '|' || coalesce(deleted_at, '') FROM entries ORDER BY id";

    private const string SheetsDump =
        "SELECT id || '|' || title || '|' || started_at || '|' || coalesce(cleared_at, '') || '|' || coalesce(deleted_at, '') FROM notepad_sheets ORDER BY id";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static TestDb CreateV110()
    {
        var db = new TestDb();
        using var c = db.Factory.Open();
        c.Execute(SchemaV110);
        c.Execute(DataV110);
        return db;
    }

    private static List<string> Query(SqliteConnection c, string sql) => [.. c.Query<string>(sql)];

    /// <summary>Видимые строки после миграции через репозиторий — в том же формате, что <see cref="VisibleV110"/>.</summary>
    private static IEnumerable<string> Visible(NotepadSheetView view) =>
        view.Lines.Select(l => $"{view.Sheet.Id}|{l.EntryId}|{l.Date:yyyy-MM-dd}|{l.StartMinute}|{l.EndMinute}|{l.Comment}");

    [Fact]
    public async Task S27_DatabaseV110_MigratesWithoutLoss_SameVisibilityAndStatuses()
    {
        using var db = CreateV110();
        List<string> visibleBefore, linksBefore, entriesBefore, sheetsBefore;
        using (var c = db.Factory.Open())
        {
            visibleBefore = Query(c, VisibleV110);
            linksBefore = Query(c, AllLinksV110);
            entriesBefore = Query(c, EntriesDump);
            sheetsBefore = Query(c, SheetsDump);
        }

        Assert.Equal(5, visibleBefore.Count); // лист 1: 1, 2; лист 3: 6, 7, 10 (лист 2 удалён, 3 и 8 убраны, 4 и 9 удалены)
        Assert.Equal(10, linksBefore.Count);
        Assert.Equal(3, await db.Migrator.GetVersionAsync(_ct));

        var result = await db.Initializer.InitializeAsync(_ct);

        Assert.Equal((3, 4), (result.Migration.FromVersion, result.Migration.ToVersion));
        Assert.NotNull(result.AutoBackupPath); // копия до миграции
        using (var c = db.Factory.Open())
        {
            Assert.Equal(entriesBefore, Query(c, EntriesDump)); // записи не тронуты ни одним байтом
            Assert.Equal(sheetsBefore, Query(c, SheetsDump));
            Assert.Equal(linksBefore, Query(c, AllLinksV120)); // каждая привязка — строка: лист, время добавления и ✕ сохранены
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM notepad_lines WHERE updated_at <> added_at"));
            Assert.Equal(0, c.ExecuteScalar<int>(
                """
                SELECT count(*) FROM notepad_lines l JOIN entries e ON e.id = l.entry_id
                WHERE l.work_date <> e.work_date OR l.start_min <> e.start_min OR l.end_min <> e.end_min OR l.comment <> e.comment
                """)); // значения скопированы из записей
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name LIKE '%notepad_items%'"));
            Assert.Equal(2, c.ExecuteScalar<int>("SELECT count(*) FROM settings"));
            Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
            Assert.Empty(c.Query("PRAGMA foreign_key_check"));
        }

        // Видимость та же, что до миграции; у всех строк статус «в статистике».
        var current = (await db.Notepad.GetCurrentAsync(_ct))!;
        var archive = await db.Notepad.GetArchiveAsync(_ct);
        Assert.Equal("Заказ: сайт кафе", current.Sheet.Title);
        Assert.Equal(visibleBefore, archive.Concat([current]).OrderBy(v => v.Sheet.Id).SelectMany(Visible).ToList());
        Assert.All(archive.Concat([current]).SelectMany(v => v.Lines), l => Assert.Equal(NotepadLineStatus.InStatistics, l.Status));
        Assert.Equal((current.TotalMinutes, false), (current.StatisticsMinutes, current.HasStatisticsDifference));
        Assert.Equal(240 + 1440 + 120, current.TotalMinutes);
        Assert.Equal([1L], archive.Select(a => a.Sheet.Id)); // удалённый из архива лист по-прежнему скрыт

        // Копия до миграции — всё ещё БД 1.1.0.
        using (var c = new SqliteConnectionFactory(result.AutoBackupPath!).Open())
        {
            Assert.Equal(visibleBefore, Query(c, VisibleV110));
            Assert.Equal(3, c.ExecuteScalar<int>("SELECT MAX(version) FROM schema_version"));
        }
    }

    [Fact]
    public async Task S27_AfterMigration_HiddenLinesComeBack_AndNotepadWorks()
    {
        using var db = CreateV110();
        await db.Initializer.InitializeAsync(_ct);
        long LineOf(long entryId)
        {
            using var c = db.Factory.Open();
            return c.ExecuteScalar<long>("SELECT id FROM notepad_lines WHERE entry_id = @entryId", new { entryId });
        }

        // Отмена удаления в «Журнале» возвращает строку (как в 1.1.0).
        await db.Entries.RestoreAsync([9], _ct);
        Assert.Contains((await db.Notepad.GetCurrentAsync(_ct))!.Lines, l => l.EntryId == 9);

        // Убранную ✕ строку можно вернуть, а правка в «Журнале» переносится в строку (F9.6).
        Assert.Equal(1, await db.Notepad.RestoreLinesAsync([LineOf(8)], _ct));
        await db.Entries.UpdateAsync(new WorkEntry(8, new DateOnly(2026, 11, 10), 510, 600, "убрана ✕, исправлена"), _ct);
        var restored = Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines, l => l.EntryId == 8);
        Assert.Equal((510, "убрана ✕, исправлена", NotepadLineStatus.InStatistics), (restored.StartMinute, restored.Comment, restored.Status));

        // Новые строки: та же запись 11:00–12:00 уже в базе — строка «не в статистике» (исходный баг больше не мешает).
        var added = await db.Notepad.AddLinesAsync([new(new EntryDraft(new DateOnly(2026, 11, 10), new TimeRange(660, 720), ""), CreateEntry: false)], _ct);
        Assert.Equal(NotepadLineStatus.NotInStatistics, added[0].Status);
        Assert.True(added[0].Id > LineOf(10)); // нумерация строк продолжается
        Assert.Equal(6, (await db.Notepad.GetCurrentAsync(_ct))!.Lines.Count); // 6, 7, 8, 9, 10 и новая
    }

    [Fact]
    public async Task Migration0004_LinkWithoutEntry_Skipped_OtherLinesMigrated()
    {
        // Привязка к несуществующей записи (в 1.1.0 невозможна при foreign_keys=ON, но БД могли править внешней
        // утилитой): данных в ней нет и в 1.1.0 она не показывалась — миграция её пропускает, а не блокирует запуск.
        using var db = CreateV110();
        using (var c = db.Factory.Open())
        {
            c.Execute("PRAGMA foreign_keys = OFF; INSERT INTO notepad_items (entry_id, sheet_id, added_at) VALUES (999, 3, '2026-11-10T09:00:00.000Z'); PRAGMA foreign_keys = ON;");
        }

        var result = await db.Migrator.MigrateAsync(ct: _ct);

        Assert.Equal((3, 4), (result.FromVersion, result.ToVersion));
        using var c2 = db.Factory.Open();
        Assert.Equal(10, c2.ExecuteScalar<int>("SELECT count(*) FROM notepad_lines")); // все 10 настоящих привязок
        Assert.Equal(0, c2.ExecuteScalar<int>("SELECT count(*) FROM notepad_lines WHERE entry_id = 999"));
        Assert.Empty(c2.Query("PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task Migration0004_LostLine_AbortsAndKeepsOldTables()
    {
        // Сверка в самой миграции: если перенос потерял строку (здесь — триггер, удаляющий одну), миграция
        // откатывается целиком, notepad_items остаётся, версия схемы прежняя.
        using var db = CreateV110();
        var sql = Migrator.Embedded.Single(m => m.Version == 4).Sql.Replace(
            "CREATE TEMP TABLE migration_0004_check",
            "DELETE FROM notepad_lines WHERE id = (SELECT max(id) FROM notepad_lines);\nCREATE TEMP TABLE migration_0004_check",
            StringComparison.Ordinal);
        Assert.Contains("DELETE FROM notepad_lines", sql, StringComparison.Ordinal);
        var migrations = Migrator.Embedded.Where(m => m.Version < 4).Append(new Migration(4, "0004_notepad_lines.sql", sql)).ToList();

        await Assert.ThrowsAsync<SqliteException>(() => db.Migrator.MigrateAsync(migrations, _ct));

        Assert.Equal(3, await db.Migrator.GetVersionAsync(_ct));
        using var c = db.Factory.Open();
        Assert.Equal(10, c.ExecuteScalar<int>("SELECT count(*) FROM notepad_items"));
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name = 'notepad_lines'"));
    }

    [Fact]
    public async Task Startup_V110_TodaysBackupExists_StillBacksUpBeforeMigration()
    {
        // Утром уже был запуск 1.1.0 (ежедневная копия есть), потом в блокноте поработали — и в тот же день
        // поставили 1.2.0: перед миграцией всё равно делается свежая копия со всеми данными схемы 1.1.0.
        using var db = CreateV110();
        var morning = await db.Backups.BackupNowAsync(_ct);
        using (var c = db.Factory.Open())
        {
            c.Execute("UPDATE notepad_items SET removed_at = '2026-11-10T13:00:00.000Z' WHERE entry_id = 10");
        }

        var result = await db.Initializer.InitializeAsync(_ct);

        Assert.Null(result.AutoBackupPath); // ежедневная копия за сегодня уже была
        Assert.NotNull(result.MigrationBackupPath);
        Assert.StartsWith(Path.Combine(db.Paths.BackupsDirectory, BackupService.BeforeMigrationFolder), result.MigrationBackupPath, StringComparison.Ordinal);
        Assert.EndsWith("worktime-v3-to-v4-2026-11-10-143700.db", result.MigrationBackupPath, StringComparison.Ordinal);
        using (var c = new SqliteConnectionFactory(result.MigrationBackupPath!).Open())
        {
            Assert.Equal(3, c.ExecuteScalar<int>("SELECT MAX(version) FROM schema_version"));
            Assert.Equal("2026-11-10T13:00:00.000Z", c.ExecuteScalar<string>("SELECT removed_at FROM notepad_items WHERE entry_id = 10"));
        }

        // Ручная копия в тот же день перезаписывает ежедневную, но не копию перед миграцией; ротация её не видит.
        Assert.Equal(morning, await db.Backups.BackupNowAsync(_ct));
        Assert.True(File.Exists(result.MigrationBackupPath));
        Assert.DoesNotContain(result.MigrationBackupPath, db.Backups.ListBackups());

        // Повторный запуск — схема уже новая, копия перед миграцией не делается.
        Assert.Null((await db.Initializer.InitializeAsync(_ct)).MigrationBackupPath);
    }

    [Fact]
    public async Task Migration0004_EmptyNotepad_V110()
    {
        using var db = new TestDb();
        using (var c = db.Factory.Open())
        {
            c.Execute(SchemaV110);
        }

        var result = await db.Migrator.MigrateAsync(ct: _ct);

        Assert.Equal((3, 4), (result.FromVersion, result.ToVersion));
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));
    }
}
