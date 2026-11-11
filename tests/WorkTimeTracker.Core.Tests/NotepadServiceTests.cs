using Microsoft.Extensions.Time.Testing;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Statistics;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>
/// F8, F9: сценарии блокнота-песочницы на хранилище в памяти (уровень сервисов).
/// Сценарии с UI и настоящей БД — <c>App.Tests/NotepadViewModelTests</c>.
/// </summary>
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

    private async Task<NotepadSheetView> CurrentAsync() => (await _notepad.GetCurrentAsync(_ct))!;

    private async Task<NotepadLine> AddOneAsync(string text)
    {
        var r = await _notepad.AddFromTextAsync(text, Today, _ct);
        Assert.True(r.IsSuccess, string.Join("; ", r.Errors));
        return Assert.Single(r.Lines);
    }

    private static EntryDraft Draft(int start, int end, string comment = "") => new(Today, new TimeRange(start, end), comment);

    private static NotepadLineStatus[] Statuses(NotepadSheetView view) => [.. view.Lines.Select(l => l.Status)];

    // ---------- F8: прежние сценарии ----------

    [Fact]
    public async Task S1_DayInPieces_SumAndJournal()
    {
        await AddOneAsync("0900 1230 задача А");
        await AddOneAsync("1330 1800 задача Б");

        var current = await CurrentAsync();
        Assert.Equal("В блокноте: 8 ч 00 мин · 2 записи · ведётся с 10 нояб., 09:12", NotepadFormat.Summary(current, TimeZoneInfo.Utc));
        Assert.Equal(["задача А", "задача Б"], _repo.Active.Select(e => e.Comment));
        Assert.Equal(480, MonthTotal());
    }

    [Fact]
    public async Task S2_MultiDayOrder_Clear_GoesToArchive_StatisticsUnchanged()
    {
        Assert.Equal("Заказ: сайт кафе", await _notepad.SetTitleAsync("  Заказ: сайт кафе ", _ct));
        await AddOneAsync("08.11 0900 1700");
        await AddOneAsync("09.11 0900 1645");
        await AddOneAsync("0900 1630");
        var statsBefore = MonthTotal();
        Assert.Equal((23 * 60) + 15, statsBefore);
        _time.Advance(TimeSpan.FromHours(9.5));

        var cleared = await _notepad.ClearAsync(_ct);

        Assert.NotNull(cleared);
        Assert.Null(await _notepad.GetCurrentAsync(_ct));
        var sheet = Assert.Single(await _notepad.GetArchiveAsync(_ct));
        Assert.Equal("Заказ: сайт кафе", NotepadFormat.SheetTitle(sheet, TimeZoneInfo.Utc));
        Assert.Equal("8–10 нояб.", NotepadFormat.DateRange(sheet));
        Assert.Equal("23 ч 15 мин · 3 записи", NotepadFormat.Totals(sheet));
        Assert.Equal("очищен 10 нояб., 18:42", NotepadFormat.Cleared(sheet, TimeZoneInfo.Utc));
        Assert.Equal(statsBefore, MonthTotal());
        Assert.Equal(3, _repo.Active.Count());
    }

    [Fact]
    public async Task S5_Remove_KeepsEntry_UndoReturns()
    {
        var line = await AddOneAsync("0900 1000");

        Assert.Equal(1, await _notepad.RemoveAsync([line.Id], _ct));
        Assert.True((await CurrentAsync()).IsEmpty);
        Assert.Single(_repo.Active);
        Assert.Equal(60, MonthTotal());

        Assert.Equal(1, await _notepad.UndoRemoveAsync([line.Id], _ct));
        Assert.Single((await CurrentAsync()).Lines);
        Assert.Equal(0, await _notepad.RemoveAsync([], _ct));
        Assert.Equal(0, await _notepad.UndoRemoveAsync([], _ct));
    }

    /// <summary>S7 (переписан в v1.2.0, F9.2): пересечение с записью журнала — не ошибка, строка «не в статистике».</summary>
    [Fact]
    public async Task S7_OverlapWithJournalEntry_LineOnlyInNotepad()
    {
        await _entries.AddFromTextAsync("0900 1200 из журнала", Today, _ct);

        var line = await AddOneAsync("1000 1100");

        Assert.Equal(NotepadLineStatus.NotInStatistics, line.Status);
        Assert.Equal("Пересекается с записью 10.11 09:00–12:00 «из журнала» — в статистику не записано", NotepadFormat.StatusHint(line));
        Assert.Single(_repo.Active);
        Assert.Equal(180, MonthTotal());
        var current = await CurrentAsync();
        Assert.Equal((60, 0), (current.TotalMinutes, current.StatisticsMinutes));
        Assert.Equal(NotepadLineStatus.NotInStatistics, Assert.Single(current.Lines).Status);
        Assert.Equal(1, Assert.Single(current.Lines).Conflict!.Id); // конфликт найден и при чтении
    }

    [Fact]
    public async Task S8_Clear_Undo_RestoresSheetWithTitle()
    {
        await _notepad.SetTitleAsync("Заказ", _ct);
        await AddOneAsync("0900 1000");

        var id = (await _notepad.ClearAsync(_ct))!.Value;
        Assert.True(await _notepad.UndoClearAsync(id, _ct));

        var current = await CurrentAsync();
        Assert.Equal(("Заказ", 1), (current.Sheet.Title, current.Lines.Count));
        Assert.Empty(await _notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task Clear_EmptyNotepad_DoesNothing()
    {
        Assert.Null(await _notepad.ClearAsync(_ct)); // листа нет
        await _notepad.SetTitleAsync("Только название", _ct);
        Assert.Null(await _notepad.ClearAsync(_ct)); // лист без строк
        Assert.Empty(await _notepad.GetArchiveAsync(_ct));
        Assert.NotNull(await _notepad.GetCurrentAsync(_ct));
    }

    [Fact]
    public async Task UndoClear_Impossible_AfterNewSheetStarted()
    {
        await AddOneAsync("0900 1000");
        var id = (await _notepad.ClearAsync(_ct))!.Value;
        await AddOneAsync("1100 1200");

        Assert.False(await _notepad.UndoClearAsync(id, _ct));
        Assert.Single(await _notepad.GetArchiveAsync(_ct));
    }

    [Fact]
    public async Task S9_DeleteSheet_KeepsEntries_UndoReturns()
    {
        await AddOneAsync("0900 1000");
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
        await AddOneAsync("0900 1000 блокнот");

        await _entries.AddFromTextAsync("1100 1200 журнал", Today, _ct);

        Assert.Equal("блокнот", Assert.Single((await CurrentAsync()).Lines).Comment);
    }

    [Fact]
    public async Task S11_S24_MultilineWithInvalidLine_NothingAdded()
    {
        var r = await _notepad.AddFromTextAsync("0900 1000\n2500 2600\n1100", Today, _ct);

        Assert.False(r.IsSuccess);
        Assert.Equal(["Строка 2: Часы должны быть от 0 до 23 — «2500»", "Строка 3: Не указано время окончания"], r.Errors);
        Assert.Empty(r.Lines);
        Assert.Empty(_repo.Active);
        Assert.Null(await _notepad.GetCurrentAsync(_ct));
    }

    [Fact]
    public async Task Add_Multiline_Form_Empty()
    {
        var many = await _notepad.AddFromTextAsync("0900 1000\n1100 1200", Today, _ct);
        Assert.Equal(2, many.Lines.Count);
        Assert.Equal(0, many.NotInStatisticsCount);
        Assert.True((await _notepad.AddFromTextAsync("  ", Today, _ct)).IsSuccess);

        var draft = _entries.BuildDraft(Today, TimeSpan.FromHours(13), TimeSpan.FromHours(14), "из формы").Value!;
        var form = await _notepad.AddAsync(draft, _ct);
        Assert.Equal(NotepadLineStatus.InStatistics, Assert.Single(form.Lines).Status);
        var again = await _notepad.AddAsync(draft, _ct); // F9.1: пересечение с самой собой — не ошибка
        Assert.Equal(NotepadLineStatus.NotInStatistics, Assert.Single(again.Lines).Status);
        Assert.Equal(1, again.NotInStatisticsCount);
        var future = await _notepad.AddAsync(draft with { Date = Today.AddDays(1) }, _ct); // ошибка формата остаётся ошибкой
        Assert.False(future.IsSuccess);
        Assert.Equal("Дата в будущем", Assert.Single(future.Errors));

        Assert.Equal(4, (await CurrentAsync()).Lines.Count);
        Assert.Equal(3, _repo.Active.Count());
    }

    [Fact]
    public async Task EntryDeletedInJournal_LeavesSheet_UndoReturns()
    {
        var line = await AddOneAsync("0900 1000");

        await _entries.DeleteAsync([line.EntryId!.Value], _ct);
        Assert.True((await CurrentAsync()).IsEmpty);

        await _entries.UndoDeleteAsync([line.EntryId!.Value], _ct);
        Assert.Single((await CurrentAsync()).Lines);
    }

    // ---------- F9: песочница ----------

    /// <summary>S13 — исходный баг: ввёл, убрал ✕, ввёл снова.</summary>
    [Fact]
    public async Task S13_AddRemoveAddAgain_SecondLineNotInStatistics()
    {
        var first = await AddOneAsync("1100 1300");
        await _notepad.RemoveAsync([first.Id], _ct);

        var second = await AddOneAsync("1100 1300");

        Assert.Equal(NotepadLineStatus.NotInStatistics, second.Status);
        var current = await CurrentAsync();
        Assert.Equal(120, current.TotalMinutes);
        Assert.Equal(120, MonthTotal()); // без задвоения
        Assert.Single(_repo.Active);
    }

    [Fact]
    public async Task S14_OverlapInsideNotepad_TwoSums()
    {
        await AddOneAsync("0900 1300");
        var second = await AddOneAsync("1200 1500");

        Assert.Equal(NotepadLineStatus.NotInStatistics, second.Status);
        var current = await CurrentAsync();
        Assert.Equal([NotepadLineStatus.InStatistics, NotepadLineStatus.NotInStatistics], Statuses(current));
        Assert.Contains("7 ч 00 мин · в статистике: 4 ч 00 мин", NotepadFormat.Summary(current, TimeZoneInfo.Utc), StringComparison.Ordinal);
        Assert.Equal(240, MonthTotal());
    }

    [Fact]
    public async Task S15_Estimates_OnlyFirstInStatistics()
    {
        await AddOneAsync("1000 1200");
        await AddOneAsync("1000 1230");
        await AddOneAsync("1000 1300");

        var current = await CurrentAsync();
        Assert.Equal([NotepadLineStatus.InStatistics, NotepadLineStatus.NotInStatistics, NotepadLineStatus.NotInStatistics], Statuses(current));
        Assert.Equal(120, current.StatisticsMinutes);
        Assert.Equal(120, MonthTotal());
    }

    [Fact]
    public async Task S16_EmptyDay_AllInStatistics_NoSecondFigure()
    {
        await AddOneAsync("0900 1000");
        await AddOneAsync("1000 1200");
        await AddOneAsync("1300 1400");

        var current = await CurrentAsync();
        Assert.All(current.Lines, l => Assert.Equal(NotepadLineStatus.InStatistics, l.Status));
        Assert.False(current.HasStatisticsDifference);
        Assert.DoesNotContain("в статистике", NotepadFormat.Summary(current, TimeZoneInfo.Utc), StringComparison.Ordinal);
    }

    [Fact]
    public async Task S17_EditLinkedLine_NoOverlap_EntryChanged()
    {
        var line = await AddOneAsync("0900 1900 работа");

        var r = await _notepad.UpdateLineAsync(line.Id, Draft(540, 1080, "работа"), _ct);

        Assert.True(r.IsSuccess);
        Assert.True(r.EntriesChanged);
        Assert.Equal(NotepadLineStatus.InStatistics, r.Line!.Status);
        Assert.Equal(1080, Assert.Single(_repo.Active).EndMinute);
        Assert.Equal(540, MonthTotal());
    }

    [Fact]
    public async Task S18_EditLinkedLine_WithOverlap_OnlyLineChanged()
    {
        await _entries.AddFromTextAsync("1800 2000 вечер", Today, _ct);
        var line = await AddOneAsync("0900 1700");
        var stats = MonthTotal();

        var r = await _notepad.UpdateLineAsync(line.Id, Draft(540, 1140), _ct);

        Assert.True(r.IsSuccess);
        Assert.False(r.EntriesChanged);
        Assert.Equal(NotepadLineStatus.DifferentInStatistics, r.Line!.Status);
        Assert.Equal("в статистике: 09:00–17:00", NotepadFormat.StatusLabel(r.Line));
        Assert.Equal("Новое время пересекается с записью 10.11 18:00–20:00 «вечер» — в статистике осталось прежнее", NotepadFormat.StatusHint(r.Line));
        Assert.Equal(stats, MonthTotal());
        Assert.Equal(1020, _repo.Active.Single(e => e.Id == line.EntryId).EndMinute);
        var current = await CurrentAsync();
        Assert.Equal((600, 480), (current.TotalMinutes, current.StatisticsMinutes));
        Assert.Equal("вечер", Assert.Single(current.Lines).Conflict!.Comment);
    }

    [Fact]
    public async Task S19_EditFreeLine_NoOverlap_EntryCreatedAndLinked()
    {
        await AddOneAsync("0900 1200");
        var free = await AddOneAsync("1100 1300");
        Assert.Equal(NotepadLineStatus.NotInStatistics, free.Status);

        var r = await _notepad.UpdateLineAsync(free.Id, Draft(720, 840), _ct);

        Assert.True(r.EntriesChanged);
        Assert.Equal(NotepadLineStatus.InStatistics, r.Line!.Status);
        Assert.NotNull(r.Line.EntryId);
        Assert.Equal(2, _repo.Active.Count());
        Assert.Equal(300, MonthTotal());
    }

    [Fact]
    public async Task S20_EditFreeLine_StillOverlaps_StaysNotInStatistics()
    {
        await AddOneAsync("0900 1200");
        var free = await AddOneAsync("1100 1300");

        var r = await _notepad.UpdateLineAsync(free.Id, Draft(600, 780, "прикидка"), _ct);

        Assert.False(r.EntriesChanged);
        Assert.Equal(NotepadLineStatus.NotInStatistics, r.Line!.Status);
        Assert.Equal((600, 780, "прикидка"), (r.Line.StartMinute, r.Line.EndMinute, r.Line.Comment));
        Assert.Single(_repo.Active);
        Assert.Equal(180, MonthTotal());
    }

    [Fact]
    public async Task S21_EditInJournal_OverridesDifferentLine()
    {
        await _entries.AddFromTextAsync("1800 2000 вечер", Today, _ct);
        var line = await AddOneAsync("0900 1700");
        await _notepad.UpdateLineAsync(line.Id, Draft(540, 1140), _ct);

        var r = await _entries.UpdateAsync(line.EntryId!.Value, Draft(480, 960, "из журнала"), _ct);

        Assert.True(r.IsSuccess);
        var synced = Assert.Single((await CurrentAsync()).Lines);
        Assert.Equal((480, 960, "из журнала"), (synced.StartMinute, synced.EndMinute, synced.Comment));
        Assert.Equal(NotepadLineStatus.InStatistics, synced.Status);
    }

    [Fact]
    public async Task S22_DeleteInJournal_LinkedLineHidden_FreeLineStays()
    {
        var linked = await AddOneAsync("1100 1300");
        var free = await AddOneAsync("1100 1300");

        await _entries.DeleteAsync([linked.EntryId!.Value], _ct);
        var afterDelete = Assert.Single((await CurrentAsync()).Lines);
        Assert.Equal(free.Id, afterDelete.Id);
        Assert.Equal(NotepadLineStatus.NotInStatistics, afterDelete.Status);
        Assert.Null(afterDelete.Conflict); // конфликта больше нет — подсказка объясняет, почему строка не записана
        Assert.Equal("В статистику не записано: при добавлении было пересечение", NotepadFormat.StatusHint(afterDelete));

        await _entries.UndoDeleteAsync([linked.EntryId!.Value], _ct);
        var current = await CurrentAsync();
        Assert.Equal([linked.Id, free.Id], current.Lines.Select(l => l.Id));
        Assert.Equal([NotepadLineStatus.InStatistics, NotepadLineStatus.NotInStatistics], Statuses(current));
    }

    [Fact]
    public async Task S23_MultilineOverlapWithEarlierLineOfSameBatch()
    {
        var r = await _notepad.AddFromTextAsync("0900 1200\n1100 1300\n1300 1500", Today, _ct);

        Assert.Equal([NotepadLineStatus.InStatistics, NotepadLineStatus.NotInStatistics, NotepadLineStatus.InStatistics], r.Lines.Select(l => l.Status));
        Assert.Equal(1, r.NotInStatisticsCount);
        Assert.Equal("Пересекается с записью 10.11 09:00–12:00 — в статистику не записано", NotepadFormat.StatusHint(r.Lines[1]));
        Assert.Equal(2, _repo.Active.Count());
        Assert.Equal(300, MonthTotal());
    }

    [Fact]
    public async Task S25_ClearWithMixedStatuses_ArchiveKeepsStatusesAndSums()
    {
        await _entries.AddFromTextAsync("1800 2000 вечер", Today, _ct);
        var linked = await AddOneAsync("0900 1700");
        await AddOneAsync("1000 1100");
        await _notepad.UpdateLineAsync(linked.Id, Draft(540, 1140), _ct);
        var stats = MonthTotal();
        var entries = _repo.Active.ToList();

        await _notepad.ClearAsync(_ct);

        var sheet = Assert.Single(await _notepad.GetArchiveAsync(_ct));
        Assert.Equal([NotepadLineStatus.DifferentInStatistics, NotepadLineStatus.NotInStatistics], Statuses(sheet));
        Assert.Equal("11 ч 00 мин · в статистике: 8 ч 00 мин · 2 записи", NotepadFormat.Totals(sheet));
        Assert.All(sheet.Lines, l => Assert.NotNull(l.Conflict));
        Assert.Equal(stats, MonthTotal());
        Assert.Equal(entries, _repo.Active.ToList());
    }

    [Fact]
    public async Task Edit_InvalidDraft_Or_MissingLine_Fails()
    {
        var line = await AddOneAsync("0900 1000");

        var future = await _notepad.UpdateLineAsync(line.Id, Draft(540, 600) with { Date = Today.AddDays(1) }, _ct);
        Assert.Equal("Дата в будущем", Assert.Single(future.Errors));
        Assert.Null(future.Line);

        var missing = await _notepad.UpdateLineAsync(999, Draft(540, 600), _ct);
        Assert.False(missing.IsSuccess);
        Assert.StartsWith("Строка не найдена", Assert.Single(missing.Errors), StringComparison.Ordinal);

        await _notepad.RemoveAsync([line.Id], _ct);
        Assert.False((await _notepad.UpdateLineAsync(line.Id, Draft(540, 660), _ct)).IsSuccess); // убранную ✕ строку не править
        Assert.Equal(600, Assert.Single(_repo.Active).EndMinute);
    }

    [Fact]
    public async Task Edit_LinkedLine_NightShift_NeighbourDayOverlap()
    {
        await _entries.AddFromTextAsync("0100 0300 ранний старт", Today, _ct);
        var line = await AddOneAsync("09.11 1800 2300");

        var r = await _notepad.UpdateLineAsync(line.Id, new EntryDraft(Today.AddDays(-1), new TimeRange(1320, 1560), ""), _ct);

        Assert.Equal(NotepadLineStatus.DifferentInStatistics, r.Line!.Status); // 22:00–02:00 заходит на 01:00 следующего дня
        Assert.Equal("ранний старт", r.Line.Conflict!.Comment);
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
