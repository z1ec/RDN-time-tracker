using Dapper;
using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F8 (данные): таблицы блокнота, ограничения, каскады и инварианты из ADR-0009.</summary>
public class NotepadRepositoryTests
{
    private const string Ts = "2026-11-10T09:00:00.000Z";
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static EntryDraft Draft(DateOnly date, int start, int end, string comment = "") => new(date, new TimeRange(start, end), comment);

    private static int Count(TestDb db, string table, string where = "1 = 1")
    {
        using var c = db.Factory.Open();
        return c.ExecuteScalar<int>($"SELECT count(*) FROM {table} WHERE {where}");
    }

    // ---------- Добавление (F8.4, F8.5, F8.8) ----------

    [Fact]
    public async Task Add_CreatesSheetLazily_AndLinksEntries()
    {
        using var db = await TestDb.CreateMigratedAsync();
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));

        var added = await db.Notepad.AddEntriesAsync([Draft(Today, 780, 1080, "Б"), Draft(Today, 540, 720, "А")], _ct);

        var current = await db.Notepad.GetCurrentAsync(_ct);
        Assert.NotNull(current);
        Assert.Equal("", current.Sheet.Title);
        Assert.True(current.Sheet.IsCurrent);
        Assert.Equal(db.Time.GetUtcNow(), current.Sheet.StartedAt);
        Assert.Equal(["А", "Б"], current.Entries.Select(e => e.Comment)); // по дате и началу
        Assert.Equal(480, current.TotalMinutes);
        Assert.Equal(2, (await db.Entries.GetAllAsync(_ct)).Count); // это обычные записи журнала
        Assert.Equal(added.Select(e => e.Id).Order(), current.Entries.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task Add_SecondTime_UsesSameSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct);
        db.Time.Advance(TimeSpan.FromHours(1));
        await db.Notepad.AddEntriesAsync([Draft(Today.AddDays(-1), 540, 600)], _ct);

        Assert.Equal(1, Count(db, "notepad_sheets"));
        var current = (await db.Notepad.GetCurrentAsync(_ct))!;
        Assert.Equal([Today.AddDays(-1), Today], current.Entries.Select(e => e.Date));
        Assert.Equal(Today.AddDays(-1), current.FirstDate);
        Assert.Equal(Today, current.LastDate);
    }

    [Fact]
    public async Task Add_IsAtomic_EntryAndLinkInOneTransaction()
    {
        using var db = await TestDb.CreateMigratedAsync();

        // Вторая запись нарушает CHECK таблицы entries — откатывается всё: и первая запись, и лист, и привязки.
        await Assert.ThrowsAsync<SqliteException>(() => db.Notepad.AddEntriesAsync(
            [Draft(Today, 540, 600, "хорошая"), Draft(Today, 700, 760, new string('я', 501))], _ct));

        Assert.Equal(0, Count(db, "entries"));
        Assert.Equal(0, Count(db, "notepad_sheets"));
        Assert.Equal(0, Count(db, "notepad_items"));
    }

    [Fact]
    public async Task Add_FailingLink_RollsBackEntry()
    {
        using var db = await TestDb.CreateMigratedAsync();
        using (var c = db.Factory.Open())
        {
            // Привязку вставить нельзя — запись тоже не должна появиться (ни в журнале, ни в статистике).
            c.Execute("CREATE TRIGGER fail_link BEFORE INSERT ON notepad_items BEGIN SELECT RAISE(ABORT, 'нет привязки'); END");
        }

        await Assert.ThrowsAsync<SqliteException>(() => db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct));

        Assert.Equal(0, Count(db, "entries"));
        Assert.Equal(0, Count(db, "notepad_sheets"));
    }

    [Fact]
    public async Task JournalEntries_DoNotAppearInNotepad()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Entries.AddAsync([Draft(Today, 540, 600, "из журнала")], _ct);
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));

        await db.Notepad.AddEntriesAsync([Draft(Today, 660, 720, "из блокнота")], _ct);
        await db.Entries.AddAsync([Draft(Today, 780, 840, "снова из журнала")], _ct);

        Assert.Equal("из блокнота", Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Entries).Comment);
    }

    // ---------- Название (F8.7) ----------

    [Fact]
    public async Task Title_CreatesSheetLazily_OnlyWhenNotEmpty()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.SetTitleAsync("", _ct);
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));

        await db.Notepad.SetTitleAsync("Заказ: сайт кафе", _ct);
        var current = (await db.Notepad.GetCurrentAsync(_ct))!;
        Assert.Equal("Заказ: сайт кафе", current.Sheet.Title);
        Assert.True(current.IsEmpty);
        Assert.Null(current.FirstDate);
        Assert.Null(current.LastDate);

        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct);
        await db.Notepad.SetTitleAsync("", _ct);
        Assert.Equal(1, Count(db, "notepad_sheets"));
        Assert.Equal("", (await db.Notepad.GetCurrentAsync(_ct))!.Sheet.Title);
    }

    // ---------- Убрать из блокнота (F8.11) ----------

    [Fact]
    public async Task RemoveItems_HidesFromSheet_KeepsEntries_RestoreBringsBack()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600, "А"), Draft(Today, 660, 720, "Б")], _ct);
        var entriesBefore = await db.Entries.GetAllAsync(_ct);

        Assert.Equal(1, await db.Notepad.RemoveItemsAsync([added[0].Id], _ct));
        Assert.Equal(0, await db.Notepad.RemoveItemsAsync([added[0].Id], _ct)); // повторно — ничего

        Assert.Equal("Б", Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Entries).Comment);
        Assert.Equal(entriesBefore, await db.Entries.GetAllAsync(_ct));

        Assert.Equal(1, await db.Notepad.RestoreItemsAsync([added[0].Id], _ct));
        Assert.Equal(2, (await db.Notepad.GetCurrentAsync(_ct))!.Entries.Count);
        Assert.Equal(0, await db.Notepad.RestoreItemsAsync([added[0].Id], _ct));
    }

    [Fact]
    public async Task RemoveItems_AffectsOnlyCurrentSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var archived = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);

        Assert.Equal(0, await db.Notepad.RemoveItemsAsync([archived[0].Id], _ct)); // архив только для чтения
        Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Entries);
    }

    // ---------- Удаление записи в журнале (F8.12, F8.16) ----------

    [Fact]
    public async Task SoftDeletedEntry_DisappearsFromSheetAndArchive_RestoreReturnsIt()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var first = await db.Notepad.AddEntriesAsync([Draft(Today.AddDays(-1), 540, 600, "архивная")], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);
        var second = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600, "текущая")], _ct);

        await db.Entries.SoftDeleteAsync([first[0].Id, second[0].Id], _ct);
        Assert.True((await db.Notepad.GetCurrentAsync(_ct))!.IsEmpty);
        var archive = Assert.Single(await db.Notepad.GetArchiveAsync(_ct)); // лист без записей остаётся в архиве
        Assert.True(archive.IsEmpty);

        await db.Entries.RestoreAsync([first[0].Id, second[0].Id], _ct);
        Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Entries);
        Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Entries);
    }

    [Fact]
    public async Task Archive_IsLive_ShowsEditedEntry()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 1140, "было")], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);

        await db.Entries.UpdateAsync(new WorkEntry(added[0].Id, Today, 540, 1080, "стало"), _ct);

        var entry = Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Entries);
        Assert.Equal((1080, "стало"), (entry.EndMinute, entry.Comment));
    }

    [Fact]
    public async Task PhysicalDeleteOfEntry_CascadesToItem_SheetStays()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600), Draft(Today, 660, 720)], _ct);
        await db.Entries.SoftDeleteAsync([added[0].Id], _ct);
        db.Time.Advance(TimeSpan.FromDays(31));

        Assert.Equal(1, await db.Entries.PurgeDeletedAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct));

        Assert.Equal(1, Count(db, "notepad_items"));
        Assert.Equal(1, Count(db, "notepad_sheets"));
        Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Entries);
    }

    // ---------- Очистить всё и архив (F8.14–F8.17) ----------

    [Fact]
    public async Task Clear_MovesSheetToArchive_EntriesUntouched()
    {
        using var db = await TestDb.CreateMigratedAsync();
        Assert.Null(await db.Notepad.ClearCurrentAsync(_ct)); // листа нет — очищать нечего
        await db.Notepad.SetTitleAsync("Заказ", _ct);
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct);
        var entriesBefore = await db.Entries.GetAllAsync(_ct);
        db.Time.Advance(TimeSpan.FromHours(2));

        var id = await db.Notepad.ClearCurrentAsync(_ct);

        Assert.NotNull(id);
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));
        var sheet = Assert.Single(await db.Notepad.GetArchiveAsync(_ct));
        Assert.Equal((id.Value, "Заказ", db.Time.GetUtcNow()), (sheet.Sheet.Id, sheet.Sheet.Title, sheet.Sheet.ClearedAt));
        Assert.False(sheet.Sheet.IsCurrent);
        Assert.Single(sheet.Entries);
        Assert.Equal(entriesBefore, await db.Entries.GetAllAsync(_ct));
    }

    [Fact]
    public async Task UndoClear_RestoresCurrentSheet_OnlyWhileNoNewSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct);
        var first = (await db.Notepad.ClearCurrentAsync(_ct))!.Value;

        Assert.True(await db.Notepad.UndoClearAsync(first, _ct));
        Assert.Empty(await db.Notepad.GetArchiveAsync(_ct));
        Assert.Equal(first, (await db.Notepad.GetCurrentAsync(_ct))!.Sheet.Id);
        Assert.False(await db.Notepad.UndoClearAsync(first, _ct)); // уже текущий

        await db.Notepad.ClearCurrentAsync(_ct);
        await db.Notepad.AddEntriesAsync([Draft(Today, 660, 720)], _ct); // создан новый текущий лист
        Assert.False(await db.Notepad.UndoClearAsync(first, _ct));
        Assert.Equal(1, Count(db, "notepad_sheets", "cleared_at IS NULL AND deleted_at IS NULL"));
        Assert.Single(await db.Notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task Archive_NewestFirst_RemovedItemsStayHidden()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var a = await db.Notepad.AddEntriesAsync([Draft(Today.AddDays(-3), 540, 600, "А1"), Draft(Today.AddDays(-2), 540, 600, "А2")], _ct);
        await db.Notepad.RemoveItemsAsync([a[1].Id], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);
        db.Time.Advance(TimeSpan.FromMinutes(5));
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600, "Б")], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);

        var archive = await db.Notepad.GetArchiveAsync(_ct);

        Assert.Equal(["Б", "А1"], archive.Select(s => s.Entries.Single().Comment));
    }

    [Fact]
    public async Task DeleteSheet_OnlyArchived_KeepsEntries_RestoreBringsBack()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600)], _ct);
        var currentId = (await db.Notepad.GetCurrentAsync(_ct))!.Sheet.Id;
        Assert.False(await db.Notepad.DeleteSheetAsync(currentId, _ct)); // текущий лист удалить нельзя
        await db.Notepad.ClearCurrentAsync(_ct);
        var entriesBefore = await db.Entries.GetAllAsync(_ct);

        Assert.True(await db.Notepad.DeleteSheetAsync(currentId, _ct));
        Assert.False(await db.Notepad.DeleteSheetAsync(currentId, _ct));
        Assert.Empty(await db.Notepad.GetArchiveAsync(_ct));
        Assert.Equal(entriesBefore, await db.Entries.GetAllAsync(_ct));
        Assert.False(await db.Notepad.UndoClearAsync(currentId, _ct)); // удалённый лист не становится текущим

        Assert.True(await db.Notepad.RestoreSheetAsync(currentId, _ct));
        Assert.False(await db.Notepad.RestoreSheetAsync(currentId, _ct));
        Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Entries);
    }

    // ---------- Очистка мусора (F8.18) ----------

    [Fact]
    public async Task Purge_RemovesOldRemovedItemsAndDeletedSheets_NeverEntries()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var old = await db.Notepad.AddEntriesAsync([Draft(Today.AddDays(-2), 540, 600, "лист 1"), Draft(Today.AddDays(-2), 660, 720, "лист 1, убрана")], _ct);
        await db.Notepad.RemoveItemsAsync([old[1].Id], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);
        var oldSheet = Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Sheet.Id;
        await db.Notepad.DeleteSheetAsync(oldSheet, _ct);

        db.Time.Advance(TimeSpan.FromDays(20));
        var recent = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600, "лист 2"), Draft(Today, 660, 720, "лист 2, убрана недавно")], _ct);
        await db.Notepad.RemoveItemsAsync([recent[1].Id], _ct);
        db.Time.Advance(TimeSpan.FromDays(11)); // первым действиям 31 день, вторым — 11

        var result = await db.Notepad.PurgeAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct);

        Assert.Equal(new NotepadPurgeResult(1, 1), result);
        Assert.Equal(0, Count(db, "notepad_sheets", $"id = {oldSheet}"));
        Assert.Equal(2, Count(db, "notepad_items")); // привязки старого листа ушли каскадом, недавно убранная — осталась
        Assert.Equal(4, Count(db, "entries")); // записи не тронуты
        Assert.Equal(4, (await db.Entries.GetAllAsync(_ct)).Count);
        Assert.False(await db.Notepad.RestoreSheetAsync(oldSheet, _ct));
        Assert.Equal(1, await db.Notepad.RestoreItemsAsync([recent[1].Id], _ct)); // недавнее ещё можно отменить
    }

    [Fact]
    public async Task Startup_PurgesNotepadGarbage()
    {
        using var db = new TestDb();
        await db.Initializer.InitializeAsync(_ct);
        var added = await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600), Draft(Today, 660, 720)], _ct);
        await db.Notepad.RemoveItemsAsync([added[0].Id], _ct);
        db.Time.Advance(TimeSpan.FromDays(31));

        var result = await db.Initializer.InitializeAsync(_ct);

        Assert.Equal(new NotepadPurgeResult(1, 0), result.PurgedNotepad);
        Assert.Equal(0, result.PurgedEntries);
        Assert.Equal(2, (await db.Entries.GetAllAsync(_ct)).Count);
    }

    // ---------- Ограничения схемы ----------

    [Fact]
    public async Task Schema_OnlyOneCurrentSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        using var c = db.Factory.Open();
        c.Execute("INSERT INTO notepad_sheets (started_at) VALUES (@Ts)", new { Ts });
        Assert.Throws<SqliteException>(() => c.Execute("INSERT INTO notepad_sheets (started_at) VALUES (@Ts)", new { Ts }));

        // Очищенных и удалённых листов может быть сколько угодно.
        c.Execute("INSERT INTO notepad_sheets (started_at, cleared_at) VALUES (@Ts, @Ts), (@Ts, @Ts)", new { Ts });
        c.Execute("INSERT INTO notepad_sheets (started_at, cleared_at, deleted_at) VALUES (@Ts, @Ts, @Ts)", new { Ts });
        Assert.Equal(4, c.ExecuteScalar<int>("SELECT count(*) FROM notepad_sheets"));
    }

    [Fact]
    public async Task Schema_TitleLength_EntryInOneSheet_ForeignKeys()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var entry = (await db.Entries.AddAsync([Draft(Today, 540, 600)], _ct))[0].Id;
        using var c = db.Factory.Open();
        c.Execute("INSERT INTO notepad_sheets (id, title, started_at, cleared_at) VALUES (1, @title, @Ts, @Ts), (2, '', @Ts, @Ts)", new { title = new string('я', 100), Ts });
        Assert.Throws<SqliteException>(() => c.Execute("UPDATE notepad_sheets SET title = @title WHERE id = 1", new { title = new string('я', 101) }));

        c.Execute("INSERT INTO notepad_items (entry_id, sheet_id, added_at) VALUES (@entry, 1, @Ts)", new { entry, Ts });
        Assert.Throws<SqliteException>(() => c.Execute("INSERT INTO notepad_items (entry_id, sheet_id, added_at) VALUES (@entry, 2, @Ts)", new { entry, Ts })); // запись — максимум в одном листе
        Assert.Throws<SqliteException>(() => c.Execute("INSERT INTO notepad_items (entry_id, sheet_id, added_at) VALUES (999, 1, @Ts)", new { Ts })); // нет такой записи
        Assert.Throws<SqliteException>(() => c.Execute("INSERT INTO notepad_items (entry_id, sheet_id, added_at) VALUES (@entry, 999, @Ts)", new { entry, Ts })); // нет такого листа
    }

    [Fact]
    public async Task Schema_DeletingSheet_CascadesToItems_NotEntries()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600), Draft(Today, 660, 720)], _ct);
        using (var c = db.Factory.Open())
        {
            c.Execute("DELETE FROM notepad_sheets");
        }

        Assert.Equal(0, Count(db, "notepad_items"));
        Assert.Equal(2, Count(db, "entries"));
    }

    [Fact]
    public async Task Data_SurvivesReopen()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.SetTitleAsync("Заказ: сайт кафе", _ct);
        await db.Notepad.AddEntriesAsync([Draft(Today, 540, 600, "сохранено")], _ct);
        SqliteConnection.ClearAllPools();

        var reopened = new NotepadRepository(new SqliteConnectionFactory(db.Paths.DatabasePath), db.Time);
        var current = (await reopened.GetCurrentAsync(_ct))!;
        Assert.Equal("Заказ: сайт кафе", current.Sheet.Title);
        Assert.Equal("сохранено", Assert.Single(current.Entries).Comment);
    }
}
