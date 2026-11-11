using Dapper;
using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F8, F9 (данные): таблицы блокнота, ограничения, каскады и инварианты из ADR-0009 и ADR-0010.</summary>
public class NotepadRepositoryTests
{
    private const string Ts = "2026-11-10T09:00:00.000Z";
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static EntryDraft Draft(DateOnly date, int start, int end, string comment = "") => new(date, new TimeRange(start, end), comment);

    /// <summary>Строки со связанными записями («в статистике»).</summary>
    private Task<IReadOnlyList<NotepadLine>> AddLinked(TestDb db, params EntryDraft[] drafts) =>
        db.Notepad.AddLinesAsync([.. drafts.Select(d => new NotepadLineDraft(d, CreateEntry: true))], _ct);

    /// <summary>Строки без записей («не в статистике»).</summary>
    private Task<IReadOnlyList<NotepadLine>> AddFree(TestDb db, params EntryDraft[] drafts) =>
        db.Notepad.AddLinesAsync([.. drafts.Select(d => new NotepadLineDraft(d, CreateEntry: false))], _ct);

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

        var added = await AddLinked(db, Draft(Today, 780, 1080, "Б"), Draft(Today, 540, 720, "А"));

        var current = await db.Notepad.GetCurrentAsync(_ct);
        Assert.NotNull(current);
        Assert.Equal("", current.Sheet.Title);
        Assert.True(current.Sheet.IsCurrent);
        Assert.Equal(db.Time.GetUtcNow(), current.Sheet.StartedAt);
        Assert.Equal(["А", "Б"], current.Lines.Select(e => e.Comment)); // по дате и началу
        Assert.Equal(480, current.TotalMinutes);
        Assert.Equal(2, (await db.Entries.GetAllAsync(_ct)).Count); // это обычные записи журнала
        Assert.Equal(added.Select(e => e.Id).Order(), current.Lines.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task Add_SecondTime_UsesSameSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await AddLinked(db, Draft(Today, 540, 600));
        db.Time.Advance(TimeSpan.FromHours(1));
        await AddLinked(db, Draft(Today.AddDays(-1), 540, 600));

        Assert.Equal(1, Count(db, "notepad_sheets"));
        var current = (await db.Notepad.GetCurrentAsync(_ct))!;
        Assert.Equal([Today.AddDays(-1), Today], current.Lines.Select(e => e.Date));
        Assert.Equal(Today.AddDays(-1), current.FirstDate);
        Assert.Equal(Today, current.LastDate);
    }

    [Fact]
    public async Task Add_IsAtomic_EntryAndLinkInOneTransaction()
    {
        using var db = await TestDb.CreateMigratedAsync();

        // Третья строка нарушает CHECK — откатывается всё: и первые строки, и запись, и лист (F9.2: одна транзакция).
        await Assert.ThrowsAsync<SqliteException>(() => db.Notepad.AddLinesAsync(
            [new(Draft(Today, 540, 600, "хорошая"), true), new(Draft(Today, 560, 600, "без записи"), false), new(Draft(Today, 700, 760, new string('я', 501)), true)], _ct));

        Assert.Equal(0, Count(db, "entries"));
        Assert.Equal(0, Count(db, "notepad_sheets"));
        Assert.Equal(0, Count(db, "notepad_lines"));
    }

    [Fact]
    public async Task Add_FailingLink_RollsBackEntry()
    {
        using var db = await TestDb.CreateMigratedAsync();
        using (var c = db.Factory.Open())
        {
            // Привязку вставить нельзя — запись тоже не должна появиться (ни в журнале, ни в статистике).
            c.Execute("CREATE TRIGGER fail_link BEFORE INSERT ON notepad_lines BEGIN SELECT RAISE(ABORT, 'нет привязки'); END");
        }

        await Assert.ThrowsAsync<SqliteException>(() => AddLinked(db, Draft(Today, 540, 600)));

        Assert.Equal(0, Count(db, "entries"));
        Assert.Equal(0, Count(db, "notepad_sheets"));
    }

    [Fact]
    public async Task JournalEntries_DoNotAppearInNotepad()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Entries.AddAsync([Draft(Today, 540, 600, "из журнала")], _ct);
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));

        await AddLinked(db, Draft(Today, 660, 720, "из блокнота"));
        await db.Entries.AddAsync([Draft(Today, 780, 840, "снова из журнала")], _ct);

        Assert.Equal("из блокнота", Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines).Comment);
    }

    // ---------- Строки блокнота (F9) ----------

    [Fact]
    public async Task AddLines_MixedLinkedAndFree_OneTransaction_Statuses()
    {
        using var db = await TestDb.CreateMigratedAsync();

        var added = await db.Notepad.AddLinesAsync(
            [new(Draft(Today, 540, 780, "в базе"), true), new(Draft(Today, 720, 900, "только блокнот"), false), new(Draft(Today, 720, 900, "дубликат"), false)], _ct);

        Assert.Equal([NotepadLineStatus.InStatistics, NotepadLineStatus.NotInStatistics, NotepadLineStatus.NotInStatistics], added.Select(l => l.Status));
        Assert.NotNull(added[0].EntryId);
        Assert.Equal("в базе", Assert.Single(await db.Entries.GetAllAsync(_ct)).Comment);
        var current = (await db.Notepad.GetCurrentAsync(_ct))!;
        Assert.Equal(["в базе", "только блокнот", "дубликат"], current.Lines.Select(l => l.Comment)); // при равном начале — по порядку ввода
        Assert.Equal((600, 240), (current.TotalMinutes, current.StatisticsMinutes));
        Assert.Equal(added, current.Lines);
        Assert.Equal(1, Count(db, "notepad_lines", "entry_id IS NOT NULL"));
        Assert.Equal(3, Count(db, "notepad_lines", $"added_at = '{EntryRepositoryTimestamp(db)}' AND updated_at = added_at"));
    }

    [Fact]
    public async Task GetLine_OnlyVisibleLinesOfCurrentSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var lines = await db.Notepad.AddLinesAsync([new(Draft(Today, 540, 600, "связана"), true), new(Draft(Today, 660, 720, "свободна"), false), new(Draft(Today, 780, 840, "убрана"), false)], _ct);
        await db.Notepad.RemoveLinesAsync([lines[2].Id], _ct);

        Assert.Equal(lines[0], await db.Notepad.GetLineAsync(lines[0].Id, _ct));
        Assert.Equal(lines[1], await db.Notepad.GetLineAsync(lines[1].Id, _ct));
        Assert.Null(await db.Notepad.GetLineAsync(lines[2].Id, _ct)); // убрана ✕
        Assert.Null(await db.Notepad.GetLineAsync(999, _ct));

        await db.Entries.SoftDeleteAsync([lines[0].EntryId!.Value], _ct);
        Assert.Null(await db.Notepad.GetLineAsync(lines[0].Id, _ct)); // запись удалена в «Журнале»

        await db.Notepad.ClearCurrentAsync(_ct);
        Assert.Null(await db.Notepad.GetLineAsync(lines[1].Id, _ct)); // архив — только для чтения
        Assert.Null(await db.Notepad.UpdateLineAsync(lines[1].Id, Draft(Today, 0, 60), NotepadLineUpdate.LineOnly, _ct));
    }

    [Fact]
    public async Task UpdateLine_LineOnly_EntryUntouched()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var line = (await AddLinked(db, Draft(Today, 540, 1020, "работа")))[0];
        var entriesBefore = await db.Entries.GetAllAsync(_ct);
        db.Time.Advance(TimeSpan.FromMinutes(5));

        var updated = await db.Notepad.UpdateLineAsync(line.Id, Draft(Today, 540, 1140, "работа"), NotepadLineUpdate.LineOnly, _ct);

        Assert.Equal(NotepadLineStatus.DifferentInStatistics, updated!.Status);
        Assert.Equal((1140, 1020), (updated.EndMinute, updated.Linked!.EndMinute));
        Assert.Equal(entriesBefore, await db.Entries.GetAllAsync(_ct));
        Assert.Equal(1, Count(db, "notepad_lines", "updated_at > added_at"));
    }

    [Fact]
    public async Task UpdateLine_LineAndEntry_BothChangedInOneTransaction()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var line = (await AddLinked(db, Draft(Today, 540, 1140, "было")))[0];

        var updated = await db.Notepad.UpdateLineAsync(line.Id, Draft(Today.AddDays(-1), 540, 1080, "стало"), NotepadLineUpdate.LineAndEntry, _ct);

        Assert.Equal(NotepadLineStatus.InStatistics, updated!.Status);
        Assert.Equal(new WorkEntry(line.EntryId!.Value, Today.AddDays(-1), 540, 1080, "стало"), Assert.Single(await db.Entries.GetAllAsync(_ct)));

        // Сбой записи откатывает и строку.
        using (var c = db.Factory.Open())
        {
            c.Execute("CREATE TRIGGER fail_entry BEFORE UPDATE ON entries BEGIN SELECT RAISE(ABORT, 'нет'); END");
        }

        await Assert.ThrowsAsync<SqliteException>(() => db.Notepad.UpdateLineAsync(line.Id, Draft(Today, 0, 60), NotepadLineUpdate.LineAndEntry, _ct));
        Assert.Equal("стало", Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines).Comment);
    }

    [Fact]
    public async Task UpdateLine_LineAndNewEntry_CreatesAndLinks()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var free = (await AddFree(db, Draft(Today, 540, 600, "черновик")))[0];
        Assert.Empty(await db.Entries.GetAllAsync(_ct));

        var updated = await db.Notepad.UpdateLineAsync(free.Id, Draft(Today, 600, 660, "в базу"), NotepadLineUpdate.LineAndNewEntry, _ct);

        Assert.Equal(NotepadLineStatus.InStatistics, updated!.Status);
        var entry = Assert.Single(await db.Entries.GetAllAsync(_ct));
        Assert.Equal((entry.Id, 600, "в базу"), (updated.EntryId!.Value, entry.StartMinute, entry.Comment));

        // Режим не подходит к строке (уже связана) — меняется только строка, новая запись не создаётся.
        var again = await db.Notepad.UpdateLineAsync(free.Id, Draft(Today, 600, 720, "в базу"), NotepadLineUpdate.LineAndNewEntry, _ct);
        Assert.Equal(NotepadLineStatus.DifferentInStatistics, again!.Status);
        Assert.Single(await db.Entries.GetAllAsync(_ct));
    }

    [Fact]
    public async Task JournalEdit_SyncsLinkedLine_EvenRemovedOrDifferent()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var lines = await AddLinked(db, Draft(Today, 540, 600, "a"), Draft(Today, 660, 720, "b"));
        await db.Notepad.UpdateLineAsync(lines[0].Id, Draft(Today, 540, 900, "a"), NotepadLineUpdate.LineOnly, _ct); // «другое время»
        await db.Notepad.RemoveLinesAsync([lines[1].Id], _ct);

        await db.Entries.UpdateAsync(new WorkEntry(lines[0].EntryId!.Value, Today, 480, 600, "a из журнала"), _ct);
        await db.Entries.UpdateAsync(new WorkEntry(lines[1].EntryId!.Value, Today, 660, 780, "b из журнала"), _ct);
        await db.Notepad.RestoreLinesAsync([lines[1].Id], _ct);

        var current = (await db.Notepad.GetCurrentAsync(_ct))!;
        Assert.Equal(["a из журнала", "b из журнала"], current.Lines.Select(l => l.Comment));
        Assert.Equal([480, 660], current.Lines.Select(l => l.StartMinute));
        Assert.All(current.Lines, l => Assert.Equal(NotepadLineStatus.InStatistics, l.Status));
    }

    [Fact]
    public async Task FreeLines_NotAffectedByJournalDelete_PurgeOnlyRemoved()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var linked = (await AddLinked(db, Draft(Today, 660, 780)))[0];
        var free = await AddFree(db, Draft(Today, 660, 780), Draft(Today, 660, 780, "убрана"));
        await db.Notepad.RemoveLinesAsync([free[1].Id], _ct);

        await db.Entries.SoftDeleteAsync([linked.EntryId!.Value], _ct);

        Assert.Equal(free[0].Id, Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines).Id);
        db.Time.Advance(TimeSpan.FromDays(31));
        await db.Entries.PurgeDeletedAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct);
        var purged = await db.Notepad.PurgeAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct);

        Assert.Equal(new NotepadPurgeResult(1, 0), purged); // убранная ✕ строка; связанная ушла каскадом с записью
        Assert.Equal(1, Count(db, "notepad_lines"));
        Assert.Equal(free[0].Id, Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines).Id);
    }

    private static string EntryRepositoryTimestamp(TestDb db) => db.Time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

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

        await AddLinked(db, Draft(Today, 540, 600));
        await db.Notepad.SetTitleAsync("", _ct);
        Assert.Equal(1, Count(db, "notepad_sheets"));
        Assert.Equal("", (await db.Notepad.GetCurrentAsync(_ct))!.Sheet.Title);
    }

    // ---------- Убрать из блокнота (F8.11) ----------

    [Fact]
    public async Task RemoveItems_HidesFromSheet_KeepsEntries_RestoreBringsBack()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await AddLinked(db, Draft(Today, 540, 600, "А"), Draft(Today, 660, 720, "Б"));
        var entriesBefore = await db.Entries.GetAllAsync(_ct);

        Assert.Equal(1, await db.Notepad.RemoveLinesAsync([added[0].Id], _ct));
        Assert.Equal(0, await db.Notepad.RemoveLinesAsync([added[0].Id], _ct)); // повторно — ничего

        Assert.Equal("Б", Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines).Comment);
        Assert.Equal(entriesBefore, await db.Entries.GetAllAsync(_ct));

        Assert.Equal(1, await db.Notepad.RestoreLinesAsync([added[0].Id], _ct));
        Assert.Equal(2, (await db.Notepad.GetCurrentAsync(_ct))!.Lines.Count);
        Assert.Equal(0, await db.Notepad.RestoreLinesAsync([added[0].Id], _ct));
    }

    [Fact]
    public async Task RemoveItems_AffectsOnlyCurrentSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var archived = await AddLinked(db, Draft(Today, 540, 600));
        await db.Notepad.ClearCurrentAsync(_ct);

        Assert.Equal(0, await db.Notepad.RemoveLinesAsync([archived[0].Id], _ct)); // архив только для чтения
        Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Lines);
    }

    // ---------- Удаление записи в журнале (F8.12, F8.16) ----------

    [Fact]
    public async Task SoftDeletedEntry_DisappearsFromSheetAndArchive_RestoreReturnsIt()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var first = await AddLinked(db, Draft(Today.AddDays(-1), 540, 600, "архивная"));
        await db.Notepad.ClearCurrentAsync(_ct);
        var second = await AddLinked(db, Draft(Today, 540, 600, "текущая"));

        await db.Entries.SoftDeleteAsync([first[0].EntryId!.Value, second[0].EntryId!.Value], _ct);
        Assert.True((await db.Notepad.GetCurrentAsync(_ct))!.IsEmpty);
        var archive = Assert.Single(await db.Notepad.GetArchiveAsync(_ct)); // лист без записей остаётся в архиве
        Assert.True(archive.IsEmpty);

        await db.Entries.RestoreAsync([first[0].EntryId!.Value, second[0].EntryId!.Value], _ct);
        Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines);
        Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Lines);
    }

    [Fact]
    public async Task Archive_IsLive_ShowsEditedEntry()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await AddLinked(db, Draft(Today, 540, 1140, "было"));
        await db.Notepad.ClearCurrentAsync(_ct);

        await db.Entries.UpdateAsync(new WorkEntry(added[0].EntryId!.Value, Today, 540, 1080, "стало"), _ct);

        var line = Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Lines);
        Assert.Equal((1080, "стало"), (line.EndMinute, line.Comment)); // F9.6: правка в «Журнале» перенесена в строку
        Assert.Equal(NotepadLineStatus.InStatistics, line.Status);
    }

    [Fact]
    public async Task PhysicalDeleteOfEntry_CascadesToItem_SheetStays()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await AddLinked(db, Draft(Today, 540, 600), Draft(Today, 660, 720));
        await db.Entries.SoftDeleteAsync([added[0].EntryId!.Value], _ct);
        db.Time.Advance(TimeSpan.FromDays(31));

        Assert.Equal(1, await db.Entries.PurgeDeletedAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct));

        Assert.Equal(1, Count(db, "notepad_lines"));
        Assert.Equal(1, Count(db, "notepad_sheets"));
        Assert.Single((await db.Notepad.GetCurrentAsync(_ct))!.Lines);
    }

    // ---------- Очистить всё и архив (F8.14–F8.17) ----------

    [Fact]
    public async Task Clear_MovesSheetToArchive_EntriesUntouched()
    {
        using var db = await TestDb.CreateMigratedAsync();
        Assert.Null(await db.Notepad.ClearCurrentAsync(_ct)); // листа нет — очищать нечего
        await db.Notepad.SetTitleAsync("Заказ", _ct);
        await AddLinked(db, Draft(Today, 540, 600));
        var entriesBefore = await db.Entries.GetAllAsync(_ct);
        db.Time.Advance(TimeSpan.FromHours(2));

        var id = await db.Notepad.ClearCurrentAsync(_ct);

        Assert.NotNull(id);
        Assert.Null(await db.Notepad.GetCurrentAsync(_ct));
        var sheet = Assert.Single(await db.Notepad.GetArchiveAsync(_ct));
        Assert.Equal((id.Value, "Заказ", db.Time.GetUtcNow()), (sheet.Sheet.Id, sheet.Sheet.Title, sheet.Sheet.ClearedAt));
        Assert.False(sheet.Sheet.IsCurrent);
        Assert.Single(sheet.Lines);
        Assert.Equal(entriesBefore, await db.Entries.GetAllAsync(_ct));
    }

    [Fact]
    public async Task UndoClear_RestoresCurrentSheet_OnlyWhileNoNewSheet()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await AddLinked(db, Draft(Today, 540, 600));
        var first = (await db.Notepad.ClearCurrentAsync(_ct))!.Value;

        Assert.True(await db.Notepad.UndoClearAsync(first, _ct));
        Assert.Empty(await db.Notepad.GetArchiveAsync(_ct));
        Assert.Equal(first, (await db.Notepad.GetCurrentAsync(_ct))!.Sheet.Id);
        Assert.False(await db.Notepad.UndoClearAsync(first, _ct)); // уже текущий

        await db.Notepad.ClearCurrentAsync(_ct);
        await AddLinked(db, Draft(Today, 660, 720)); // создан новый текущий лист
        Assert.False(await db.Notepad.UndoClearAsync(first, _ct));
        Assert.Equal(1, Count(db, "notepad_sheets", "cleared_at IS NULL AND deleted_at IS NULL"));
        Assert.Single(await db.Notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task Archive_NewestFirst_RemovedItemsStayHidden()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var a = await AddLinked(db, Draft(Today.AddDays(-3), 540, 600, "А1"), Draft(Today.AddDays(-2), 540, 600, "А2"));
        await db.Notepad.RemoveLinesAsync([a[1].Id], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);
        db.Time.Advance(TimeSpan.FromMinutes(5));
        await AddLinked(db, Draft(Today, 540, 600, "Б"));
        await db.Notepad.ClearCurrentAsync(_ct);

        var archive = await db.Notepad.GetArchiveAsync(_ct);

        Assert.Equal(["Б", "А1"], archive.Select(s => s.Lines.Single().Comment));
    }

    [Fact]
    public async Task DeleteSheet_OnlyArchived_KeepsEntries_RestoreBringsBack()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await AddLinked(db, Draft(Today, 540, 600));
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
        Assert.Single(Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Lines);
    }

    // ---------- Очистка мусора (F8.18) ----------

    [Fact]
    public async Task Purge_RemovesOldRemovedItemsAndDeletedSheets_NeverEntries()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var old = await AddLinked(db, Draft(Today.AddDays(-2), 540, 600, "лист 1"), Draft(Today.AddDays(-2), 660, 720, "лист 1, убрана"));
        await db.Notepad.RemoveLinesAsync([old[1].Id], _ct);
        await db.Notepad.ClearCurrentAsync(_ct);
        var oldSheet = Assert.Single(await db.Notepad.GetArchiveAsync(_ct)).Sheet.Id;
        await db.Notepad.DeleteSheetAsync(oldSheet, _ct);

        db.Time.Advance(TimeSpan.FromDays(20));
        var recent = await AddLinked(db, Draft(Today, 540, 600, "лист 2"), Draft(Today, 660, 720, "лист 2, убрана недавно"));
        await db.Notepad.RemoveLinesAsync([recent[1].Id], _ct);
        db.Time.Advance(TimeSpan.FromDays(11)); // первым действиям 31 день, вторым — 11

        var result = await db.Notepad.PurgeAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct);

        Assert.Equal(new NotepadPurgeResult(1, 1), result);
        Assert.Equal(0, Count(db, "notepad_sheets", $"id = {oldSheet}"));
        Assert.Equal(2, Count(db, "notepad_lines")); // привязки старого листа ушли каскадом, недавно убранная — осталась
        Assert.Equal(4, Count(db, "entries")); // записи не тронуты
        Assert.Equal(4, (await db.Entries.GetAllAsync(_ct)).Count);
        Assert.False(await db.Notepad.RestoreSheetAsync(oldSheet, _ct));
        Assert.Equal(1, await db.Notepad.RestoreLinesAsync([recent[1].Id], _ct)); // недавнее ещё можно отменить
    }

    [Fact]
    public async Task Startup_PurgesNotepadGarbage()
    {
        using var db = new TestDb();
        await db.Initializer.InitializeAsync(_ct);
        var added = await AddLinked(db, Draft(Today, 540, 600), Draft(Today, 660, 720));
        await db.Notepad.RemoveLinesAsync([added[0].Id], _ct);
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
    public async Task Schema_TitleLength_EntryInOneLine_Checks_ForeignKeys()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var entry = (await db.Entries.AddAsync([Draft(Today, 540, 600)], _ct))[0].Id;
        using var c = db.Factory.Open();
        c.Execute("INSERT INTO notepad_sheets (id, title, started_at, cleared_at) VALUES (1, @title, @Ts, @Ts), (2, '', @Ts, @Ts)", new { title = new string('я', 100), Ts });
        Assert.Throws<SqliteException>(() => c.Execute("UPDATE notepad_sheets SET title = @title WHERE id = 1", new { title = new string('я', 101) }));

        const string Insert = "INSERT INTO notepad_lines (sheet_id, work_date, start_min, end_min, comment, entry_id, added_at, updated_at) VALUES (@sheet, '2026-11-10', @start, @end, @comment, @entry, @Ts, @Ts)";
        object Line(long sheet = 1, long? entry = null, int start = 540, int end = 600, string comment = "") => new { sheet, entry, start, end, comment, Ts };

        c.Execute(Insert, Line(entry: entry));
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(sheet: 2, entry: entry))); // запись связана максимум с одной строкой
        c.Execute(Insert, Line());
        c.Execute(Insert, Line(sheet: 2)); // несвязанных строк с тем же временем — сколько угодно (F9.1)
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(entry: 999))); // нет такой записи
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(sheet: 999))); // нет такого листа
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(start: 600, end: 600))); // нулевая длительность
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(start: 1440, end: 1500))); // начало вне суток
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(start: 0, end: 1441))); // дольше суток
        Assert.Throws<SqliteException>(() => c.Execute(Insert, Line(comment: new string('я', 501))));
        Assert.Equal(3, c.ExecuteScalar<int>("SELECT count(*) FROM notepad_lines"));
    }

    [Fact]
    public async Task Schema_DeletingSheet_CascadesToLines_NotEntries()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await AddLinked(db, Draft(Today, 540, 600), Draft(Today, 660, 720));
        using (var c = db.Factory.Open())
        {
            c.Execute("DELETE FROM notepad_sheets");
        }

        Assert.Equal(0, Count(db, "notepad_lines"));
        Assert.Equal(2, Count(db, "entries"));
    }

    [Fact]
    public async Task Data_SurvivesReopen()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Notepad.SetTitleAsync("Заказ: сайт кафе", _ct);
        await AddLinked(db, Draft(Today, 540, 600, "сохранено"));
        SqliteConnection.ClearAllPools();

        var reopened = new NotepadRepository(new SqliteConnectionFactory(db.Paths.DatabasePath), db.Time);
        var current = (await reopened.GetCurrentAsync(_ct))!;
        Assert.Equal("Заказ: сайт кафе", current.Sheet.Title);
        Assert.Equal("сохранено", Assert.Single(current.Lines).Comment);
    }
}
