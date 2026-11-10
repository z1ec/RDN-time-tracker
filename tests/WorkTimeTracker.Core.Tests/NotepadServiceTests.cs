using Microsoft.Extensions.Time.Testing;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Statistics;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F8: сценарии блокнота на хранилище в памяти (S1, S2, S5, S7, S8, S9, S10, S11 — уровень сервисов).</summary>
public class NotepadServiceTests
{
    private readonly InMemoryEntryRepository _repo = new();
    private readonly FakeTimeProvider _time = Create(hour: 9, minute: 12);
    private readonly EntryService _entries;
    private readonly NotepadService _notepad;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public NotepadServiceTests()
    {
        _entries = new EntryService(_repo, _time);
        _notepad = new NotepadService(new InMemoryNotepadRepository(_repo, _time), _entries);
    }

    private int MonthTotal() =>
        StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Month, Today), [.. _repo.Active], Today, new AppSettings()).TotalMinutes;

    [Fact]
    public async Task S1_DayInPieces_SumAndJournal()
    {
        Assert.True((await _notepad.AddFromTextAsync("0900 1230 задача А", Today, _ct)).IsSuccess);
        Assert.True((await _notepad.AddFromTextAsync("1330 1800 задача Б", Today, _ct)).IsSuccess);

        var current = (await _notepad.GetCurrentAsync(_ct))!;
        Assert.Equal("В блокноте: 8 ч 00 мин · 2 записи · ведётся с 10 нояб., 09:12", NotepadFormat.Summary(current, TimeZoneInfo.Utc));
        Assert.Equal(["задача А", "задача Б"], _repo.Active.Select(e => e.Comment));
        Assert.Equal(480, MonthTotal());
    }

    [Fact]
    public async Task S2_MultiDayOrder_Clear_GoesToArchive_StatisticsUnchanged()
    {
        Assert.Equal("Заказ: сайт кафе", await _notepad.SetTitleAsync("  Заказ: сайт кафе ", _ct));
        await _notepad.AddFromTextAsync("08.11 0900 1700", Today, _ct);
        await _notepad.AddFromTextAsync("09.11 0900 1645", Today, _ct);
        await _notepad.AddFromTextAsync("0900 1630", Today, _ct);
        var statsBefore = MonthTotal();
        Assert.Equal((23 * 60) + 15, statsBefore);
        _time.Advance(TimeSpan.FromHours(9.5));

        var cleared = await _notepad.ClearAsync(_ct);

        Assert.NotNull(cleared);
        Assert.Null(await _notepad.GetCurrentAsync(_ct));
        var sheet = Assert.Single(await _notepad.GetArchiveAsync(_ct));
        Assert.Equal("Заказ: сайт кафе", NotepadFormat.SheetTitle(sheet, TimeZoneInfo.Utc));
        Assert.Equal("8–10 нояб.", NotepadFormat.DateRange(sheet));
        Assert.Equal("23 ч 15 мин · 3 записи", NotepadFormat.Totals(sheet.TotalMinutes, sheet.Entries.Count));
        Assert.Equal("очищен 10 нояб., 18:42", NotepadFormat.Cleared(sheet, TimeZoneInfo.Utc));
        Assert.Equal(statsBefore, MonthTotal());
        Assert.Equal(3, _repo.Active.Count());
    }

    [Fact]
    public async Task S5_Remove_KeepsEntry_UndoReturns()
    {
        var id = (await _notepad.AddFromTextAsync("0900 1000", Today, _ct)).Entries[0].Id;

        Assert.Equal(1, await _notepad.RemoveAsync([id], _ct));
        Assert.True((await _notepad.GetCurrentAsync(_ct))!.IsEmpty);
        Assert.Single(_repo.Active);
        Assert.Equal(60, MonthTotal());

        Assert.Equal(1, await _notepad.UndoRemoveAsync([id], _ct));
        Assert.Single((await _notepad.GetCurrentAsync(_ct))!.Entries);
        Assert.Equal(0, await _notepad.RemoveAsync([], _ct));
        Assert.Equal(0, await _notepad.UndoRemoveAsync([], _ct));
    }

    [Fact]
    public async Task S7_Overlap_WithJournalEntry_NothingAdded()
    {
        await _entries.AddFromTextAsync("0900 1200 из журнала", Today, _ct);

        var r = await _notepad.AddFromTextAsync("1000 1100", Today, _ct);

        Assert.False(r.IsSuccess);
        Assert.StartsWith("Пересекается с записью 10.11 09:00–12:00", Assert.Single(r.Errors), StringComparison.Ordinal);
        Assert.Single(_repo.Active);
        Assert.Null(await _notepad.GetCurrentAsync(_ct)); // лист не создан
    }

    [Fact]
    public async Task S8_Clear_Undo_RestoresSheetWithTitle()
    {
        await _notepad.SetTitleAsync("Заказ", _ct);
        await _notepad.AddFromTextAsync("0900 1000", Today, _ct);

        var id = (await _notepad.ClearAsync(_ct))!.Value;
        Assert.True(await _notepad.UndoClearAsync(id, _ct));

        var current = (await _notepad.GetCurrentAsync(_ct))!;
        Assert.Equal(("Заказ", 1), (current.Sheet.Title, current.Entries.Count));
        Assert.Empty(await _notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task Clear_EmptyNotepad_DoesNothing()
    {
        Assert.Null(await _notepad.ClearAsync(_ct)); // листа нет
        await _notepad.SetTitleAsync("Только название", _ct);
        Assert.Null(await _notepad.ClearAsync(_ct)); // лист без записей
        Assert.Empty(await _notepad.GetArchiveAsync(_ct));
        Assert.NotNull(await _notepad.GetCurrentAsync(_ct));
    }

    [Fact]
    public async Task UndoClear_Impossible_AfterNewSheetStarted()
    {
        await _notepad.AddFromTextAsync("0900 1000", Today, _ct);
        var id = (await _notepad.ClearAsync(_ct))!.Value;
        await _notepad.AddFromTextAsync("1100 1200", Today, _ct);

        Assert.False(await _notepad.UndoClearAsync(id, _ct));
        Assert.Single(await _notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task S9_DeleteSheet_KeepsEntries_UndoReturns()
    {
        await _notepad.AddFromTextAsync("0900 1000", Today, _ct);
        var id = (await _notepad.ClearAsync(_ct))!.Value;

        Assert.True(await _notepad.DeleteSheetAsync(id, _ct));
        Assert.Empty(await _notepad.GetArchiveAsync(_ct));
        Assert.Single(_repo.Active);
        Assert.Equal(60, MonthTotal());

        Assert.True(await _notepad.UndoDeleteSheetAsync(id, _ct));
        Assert.Single(await _notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task S10_JournalEntry_DoesNotChangeNotepad()
    {
        await _notepad.AddFromTextAsync("0900 1000 блокнот", Today, _ct);

        await _entries.AddFromTextAsync("1100 1200 журнал", Today, _ct);

        Assert.Equal("блокнот", Assert.Single((await _notepad.GetCurrentAsync(_ct))!.Entries).Comment);
    }

    [Fact]
    public async Task S11_MultilineWithInvalidLine_NothingAdded()
    {
        var r = await _notepad.AddFromTextAsync("0900 1000\n2500 2600\n1100", Today, _ct);

        Assert.False(r.IsSuccess);
        Assert.Equal(["Строка 2: Часы должны быть от 0 до 23 — «2500»", "Строка 3: Не указано время окончания"], r.Errors);
        Assert.Empty(_repo.Active);
        Assert.Null(await _notepad.GetCurrentAsync(_ct));
    }

    [Fact]
    public async Task Add_Multiline_Form_Empty()
    {
        var many = await _notepad.AddFromTextAsync("0900 1000\n1100 1200", Today, _ct);
        Assert.Equal(2, many.Entries.Count);
        Assert.True((await _notepad.AddFromTextAsync("  ", Today, _ct)).IsSuccess);

        var draft = _entries.BuildDraft(Today, TimeSpan.FromHours(13), TimeSpan.FromHours(14), "из формы").Value!;
        Assert.True((await _notepad.AddAsync(draft, _ct)).IsSuccess);
        Assert.False((await _notepad.AddAsync(draft, _ct)).IsSuccess); // пересечение с самой собой
        Assert.False((await _notepad.AddAsync(draft with { Date = Today.AddDays(1) }, _ct)).IsSuccess); // будущее

        Assert.Equal(3, (await _notepad.GetCurrentAsync(_ct))!.Entries.Count);
        Assert.Equal(3, _repo.Active.Count());
    }

    [Fact]
    public async Task EntryDeletedInJournal_LeavesSheet_UndoReturns()
    {
        var id = (await _notepad.AddFromTextAsync("0900 1000", Today, _ct)).Entries[0].Id;

        await _entries.DeleteAsync([id], _ct);
        Assert.True((await _notepad.GetCurrentAsync(_ct))!.IsEmpty);

        await _entries.UndoDeleteAsync([id], _ct);
        Assert.Single((await _notepad.GetCurrentAsync(_ct))!.Entries);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  Заказ  ", "Заказ")]
    public void NormalizeTitle_Trims(string? input, string expected) => Assert.Equal(expected, NotepadService.NormalizeTitle(input));

    [Fact]
    public void NormalizeTitle_CutsTo100()
    {
        Assert.Equal(100, NotepadService.NormalizeTitle(new string('я', 150)).Length);
        Assert.Equal(new string('я', 99), NotepadService.NormalizeTitle(new string('я', 99) + " " + new string('ю', 10)));
    }
}
