using Dapper;

namespace WorkTimeTracker.Data.Tests;

/// <summary>
/// F8 / F4.4: обновление настоящей БД версии 1.0.0 (схема v2) до текущей схемы (цепочка 1.0.0 → 1.2.0).
/// Фикстура — замороженная копия схемы 1.0.0 и данные, как их записывала версия 1.0.0.
/// Обновление с 1.1.0 (S27) — <see cref="NotepadLinesMigrationTests"/>.
/// </summary>
public class NotepadMigrationTests
{
    /// <summary>Схема БД WorkTime Tracker 1.0.0 (миграции 0001 и 0002) — намеренно продублирована, а не взята из сборки.</summary>
    private const string SchemaV100 =
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
        INSERT INTO schema_version (version, applied_at) VALUES (1, '2026-11-09T08:00:00.0000000+00:00'), (2, '2026-11-09T08:00:00.0000000+00:00');
        """;

    private const string DataV100 =
        """
        INSERT INTO entries (id, work_date, start_min, end_min, comment, created_at, updated_at, deleted_at) VALUES
          (1, '2026-11-02', 540, 1080, 'планёрка и отчёт',  '2026-11-02T15:00:00.000Z', '2026-11-02T15:00:00.000Z', NULL),
          (2, '2026-11-03', 1320, 1560, 'ночная смена',      '2026-11-04T02:05:00.000Z', '2026-11-04T02:05:00.000Z', NULL),
          (3, '2026-11-05', 0, 1440, 'дежурство, сутки',     '2026-11-06T00:01:00.000Z', '2026-11-06T09:30:00.000Z', NULL),
          (4, '2026-11-06', 600, 660, 'удалённая запись',    '2026-11-06T11:00:00.000Z', '2026-11-08T10:00:00.000Z', '2026-11-08T10:00:00.000Z'),
          (7, '2026-11-09', 555, 1065, 'Созвон: «кавычки»; запятая, перенос' || char(10) || 'вторая строка', '2026-11-09T14:45:00.000Z', '2026-11-09T14:45:00.000Z', NULL);
        INSERT INTO settings (key, value) VALUES ('daily_norm_minutes', '450'), ('work_days', '1,2,3,4,6'), ('theme', 'Dark'), ('window', '10,20,1200,780,0');
        """;

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private sealed record EntryDump(long Id, string WorkDate, long StartMin, long EndMin, string Comment, string CreatedAt, string UpdatedAt, string? DeletedAt);

    private static List<EntryDump> Dump(Microsoft.Data.Sqlite.SqliteConnection c) =>
        [.. c.Query<EntryDump>(
            "SELECT id AS Id, work_date AS WorkDate, start_min AS StartMin, end_min AS EndMin, comment AS Comment, created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt FROM entries ORDER BY id")];

    private static TestDb CreateV100()
    {
        var db = new TestDb();
        using var c = db.Factory.Open();
        c.Execute(SchemaV100);
        c.Execute(DataV100);
        return db;
    }

    [Fact]
    public async Task DatabaseV100_WithData_MigratesAndKeepsEverything()
    {
        using var db = CreateV100();
        List<EntryDump> before;
        using (var c = db.Factory.Open())
        {
            before = Dump(c);
        }

        Assert.Equal(5, before.Count);
        Assert.Equal(2, await db.Migrator.GetVersionAsync(_ct));

        var result = await db.Initializer.InitializeAsync(_ct);

        Assert.Equal(2, result.Migration.FromVersion);
        Assert.Equal(Migrator.LatestVersion, result.Migration.ToVersion);
        Assert.Equal(4, Migrator.LatestVersion);
        Assert.NotNull(result.AutoBackupPath); // копия сделана до миграции
        using (var c = db.Factory.Open())
        {
            Assert.Equal(before, Dump(c));
            Assert.Equal(4, c.ExecuteScalar<int>("SELECT count(*) FROM settings"));
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM notepad_sheets"));
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM notepad_lines"));
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name = 'notepad_items'"));
            Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
            Assert.Empty(c.Query("PRAGMA foreign_key_check"));
        }

        // Копия до миграции — всё ещё БД версии 1.0.0 с теми же записями.
        var backupFactory = new SqliteConnectionFactory(result.AutoBackupPath!);
        using (var c = backupFactory.Open())
        {
            Assert.Equal(before, Dump(c));
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'notepad%'"));
        }

        // Приложение читает старые данные как раньше: 4 активные записи, настройки на месте.
        var all = await db.Entries.GetAllAsync(_ct);
        Assert.Equal([1L, 2L, 3L, 7L], all.Select(e => e.Id));
        Assert.Equal(1560, all[1].EndMinute);
        Assert.Contains("вторая строка", all[3].Comment, StringComparison.Ordinal);
        var settings = await db.Settings.LoadAsync(_ct);
        Assert.Equal(450, settings.DailyNormMinutes);
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));
        Assert.Empty(await db.Notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task DatabaseV100_AfterMigration_NotepadWorksWithOldEntries()
    {
        using var db = CreateV100();
        await db.Initializer.InitializeAsync(_ct);

        var added = await db.Notepad.AddLinesAsync(
            [new(new Core.Model.EntryDraft(new DateOnly(2026, 11, 10), new Core.Model.TimeRange(540, 600), "новая"), CreateEntry: true)], _ct);

        Assert.Equal(8, added[0].EntryId); // AUTOINCREMENT продолжает нумерацию старой БД
        Assert.Equal(5, (await db.Entries.GetAllAsync(_ct)).Count);
        Assert.Equal("новая", Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines).Comment);
    }

    [Fact]
    public async Task CurrentSchema_TablesAndIndexes()
    {
        using var db = await TestDb.CreateMigratedAsync();
        using var c = db.Factory.Open();
        var objects = c.Query<string>("SELECT name FROM sqlite_master").ToList();
        Assert.Contains("notepad_sheets", objects);
        Assert.Contains("notepad_lines", objects);
        Assert.DoesNotContain("notepad_items", objects);
        Assert.DoesNotContain("ix_notepad_items_sheet", objects);
        Assert.Contains("ix_notepad_lines_sheet", objects);
        Assert.Contains("ux_notepad_lines_entry", objects);
        Assert.Contains("ux_notepad_sheets_current", objects);
        Assert.Equal("0003_notepad.sql", Migrator.Embedded[2].Name);
        Assert.Equal("0004_notepad_lines.sql", Migrator.Embedded[3].Name);
        var entryColumns = c.Query<string>("SELECT name FROM pragma_table_info('entries')").ToList();
        Assert.Equal(["id", "work_date", "start_min", "end_min", "comment", "created_at", "updated_at", "deleted_at"], entryColumns);
        var lineColumns = c.Query<string>("SELECT name FROM pragma_table_info('notepad_lines')").ToList();
        Assert.Equal(["id", "sheet_id", "work_date", "start_min", "end_min", "comment", "entry_id", "added_at", "updated_at", "removed_at"], lineColumns);
    }
}
