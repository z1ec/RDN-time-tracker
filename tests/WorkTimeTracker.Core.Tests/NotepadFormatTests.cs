using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F8.6, F8.15, F8.16, F9.3, F9.4: тексты плашки, карточек архива и статусов строк.</summary>
public class NotepadFormatTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 3, 6, 12, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.CreateCustomTimeZone("msk", TimeSpan.FromHours(3), "msk", "msk");

    private static NotepadSheetView View(string title, DateTimeOffset? cleared, params NotepadLine[] lines) =>
        new(new NotepadSheet(1, title, Started, cleared), lines);

    /// <summary>Строка «в статистике»: связана с записью с теми же значениями.</summary>
    private static NotepadLine E(string date, int start, int end) => new(1, D(date), start, end, "", new WorkEntry(10, D(date), start, end, ""));

    /// <summary>Строка «не в статистике».</summary>
    private static NotepadLine Free(string date, int start, int end, WorkEntry? conflict = null) =>
        new(2, D(date), start, end, "", null) { Conflict = conflict };

    // ---------- Статусы (F9.3) ----------

    [Fact]
    public void Status_ComputedFromLineAndLinkedEntry()
    {
        var entry = new WorkEntry(7, Today, 540, 1020, "работа");
        var line = new NotepadLine(1, Today, 540, 1020, "работа", entry);
        Assert.Equal(NotepadLineStatus.InStatistics, line.Status);
        Assert.Equal(7, line.EntryId);
        Assert.Equal(NotepadLineStatus.NotInStatistics, (line with { Linked = null }).Status);
        Assert.Null((line with { Linked = null }).EntryId);
        Assert.Equal(NotepadLineStatus.DifferentInStatistics, (line with { EndMinute = 1140 }).Status);
        Assert.Equal(NotepadLineStatus.DifferentInStatistics, (line with { Comment = "Работа" }).Status); // регистр важен
        Assert.Equal(NotepadLineStatus.DifferentInStatistics, (line with { Date = Today.AddDays(-1) }).Status);
        Assert.Equal(NotepadLineStatus.DifferentInStatistics, (line with { StartMinute = 480 }).Status);
        Assert.Equal(new EntryDraft(Today, new TimeRange(540, 1020), "работа"), line.ToDraft());
        Assert.Equal(new WorkEntry(1, Today, 540, 1020, "работа"), line.Values);
        Assert.False(line.EndsNextDay);
        Assert.True((line with { EndMinute = 1500 }).EndsNextDay);
    }

    [Fact]
    public void StatusLabelAndHint_InStatistics()
    {
        var line = E("10.11.2026", 540, 600);
        Assert.Equal("", NotepadFormat.StatusLabel(line));
        Assert.Equal("Учтено в журнале и статистике", NotepadFormat.StatusHint(line));
        Assert.Equal("Строка изменена · в статистике", NotepadFormat.Edited(line));
    }

    [Fact]
    public void StatusLabelAndHint_NotInStatistics_WithAndWithoutConflict()
    {
        var conflict = new WorkEntry(3, D("11.11.2026"), 660, 780, "созвон с клиентом");
        var line = Free("11.11.2026", 660, 780, conflict);
        Assert.Equal("не в статистике", NotepadFormat.StatusLabel(line));
        Assert.Equal("Пересекается с записью 11.11 11:00–13:00 «созвон с клиентом» — в статистику не записано", NotepadFormat.StatusHint(line));
        Assert.Equal("В статистику не записано: при добавлении было пересечение", NotepadFormat.StatusHint(line with { Conflict = null }));
        Assert.Equal(
            "Строка изменена только в блокноте · Пересекается с записью 11.11 11:00–13:00 «созвон с клиентом» — в статистику не записано",
            NotepadFormat.Edited(line));
    }

    [Fact]
    public void StatusLabelAndHint_DifferentInStatistics()
    {
        var entry = new WorkEntry(7, Today, 540, 1020, "");
        var line = new NotepadLine(1, Today, 540, 1140, "", entry) { Conflict = new WorkEntry(8, Today, 1080, 1200, "") };
        Assert.Equal("в статистике: 09:00–17:00", NotepadFormat.StatusLabel(line));
        Assert.Equal("Новое время пересекается с записью 10.11 18:00–20:00 — в статистике осталось прежнее", NotepadFormat.StatusHint(line));
        Assert.Equal("Новое время не записано в статистику: при правке было пересечение. В статистике осталось прежнее", NotepadFormat.StatusHint(line with { Conflict = null }));
        Assert.Equal("в статистике: 10.11 09:00–17:00", NotepadFormat.StatusLabel(line with { Date = Today.AddDays(-1) })); // дата отличается — видна
    }

    [Fact]
    public void Added_CountsNotInStatistics()
    {
        Assert.Equal("Добавлено: 1 запись", NotepadFormat.Added(1, 0));
        Assert.Equal("Добавлено: 3 записи · не в статистике: 1 (пересечение с журналом)", NotepadFormat.Added(3, 1));
    }

    // ---------- Две суммы (F9.4) ----------

    [Fact]
    public void Totals_SecondFigure_OnlyWhenSumsDiffer()
    {
        var same = View("", null, E("10.11.2026", 540, 780), E("10.11.2026", 780, 960));
        Assert.Equal((420, 420, false), (same.TotalMinutes, same.StatisticsMinutes, same.HasStatisticsDifference));
        Assert.Equal("7 ч 00 мин · 2 записи", NotepadFormat.Totals(same));
        Assert.Equal("", NotepadFormat.SummaryHint(same));
        Assert.Equal("", NotepadFormat.SummaryHint(null));

        // S14: 09:00–13:00 в статистике, 12:00–15:00 — только в блокноте.
        var differ = View("", null, E("10.11.2026", 540, 780), Free("10.11.2026", 720, 900));
        Assert.Equal((420, 240, true), (differ.TotalMinutes, differ.StatisticsMinutes, differ.HasStatisticsDifference));
        Assert.Equal("7 ч 00 мин · в статистике: 4 ч 00 мин · 2 записи", NotepadFormat.Totals(differ));
        Assert.Equal("В блокноте: 7 ч 00 мин · в статистике: 4 ч 00 мин · 2 записи · ведётся с 3 нояб., 09:12", NotepadFormat.Summary(differ, Moscow));
        Assert.StartsWith("Часть строк не записана в статистику", NotepadFormat.SummaryHint(differ), StringComparison.Ordinal);

        // «В статистике другое время»: считается фактическое время записи в базе.
        var edited = View("", null, new NotepadLine(1, Today, 540, 1140, "", new WorkEntry(7, Today, 540, 1020, "")));
        Assert.Equal("10 ч 00 мин · в статистике: 8 ч 00 мин · 1 запись", NotepadFormat.Totals(edited));
    }

    [Fact]
    public void Summary_NoSheet_Empty_WithEntries()
    {
        Assert.Equal("В блокноте: 0 ч 00 мин · 0 записей", NotepadFormat.Summary(null, Moscow));
        Assert.Equal("В блокноте: 0 ч 00 мин · 0 записей · ведётся с 3 нояб., 09:12", NotepadFormat.Summary(View("", null), Moscow));
        Assert.Equal(
            "В блокноте: 7 ч 30 мин · 2 записи · ведётся с 3 нояб., 09:12",
            NotepadFormat.Summary(View("", null, E("03.11.2026", 540, 720), E("03.11.2026", 810, 1080)), Moscow));
    }

    [Fact]
    public void Summary_NightShift_CountsWhole()
    {
        var view = View("", null, E("03.11.2026", 1320, 1560));
        Assert.Equal(240, view.TotalMinutes);
        Assert.StartsWith("В блокноте: 4 ч 00 мин · 1 запись", NotepadFormat.Summary(view, Moscow), StringComparison.Ordinal);
    }

    [Fact]
    public void SheetTitle_Named_ByDates_ByClearedDate()
    {
        var cleared = new DateTimeOffset(2026, 11, 6, 21, 40, 0, TimeSpan.Zero); // 7 нояб. 00:40 по Москве
        Assert.Equal("Заказ: сайт кафе", NotepadFormat.SheetTitle(View("Заказ: сайт кафе", cleared, E("03.11.2026", 0, 60)), Moscow));
        Assert.Equal("Лист 3–6 нояб.", NotepadFormat.SheetTitle(View("", cleared, E("06.11.2026", 0, 60), E("03.11.2026", 0, 60)), Moscow));
        Assert.Equal("Лист 6 нояб.", NotepadFormat.SheetTitle(View("", cleared, E("06.11.2026", 0, 60)), Moscow));
        Assert.Equal("Лист от 7 нояб.", NotepadFormat.SheetTitle(View("", cleared), Moscow));
        Assert.Equal("Лист от 3 нояб.", NotepadFormat.SheetTitle(View("", null), Moscow));
    }

    [Fact]
    public void DateRange_Cleared_Totals()
    {
        var cleared = new DateTimeOffset(2026, 11, 6, 15, 40, 0, TimeSpan.Zero);
        Assert.Equal("3–6 нояб.", NotepadFormat.DateRange(View("", cleared, E("03.11.2026", 0, 60), E("06.11.2026", 0, 60))));
        Assert.Equal("записи удалены", NotepadFormat.DateRange(View("", cleared)));
        Assert.Equal("очищен 6 нояб., 18:40", NotepadFormat.Cleared(View("", cleared), Moscow));
        Assert.Equal("", NotepadFormat.Cleared(View("", null), Moscow));
        Assert.Equal("0 ч 00 мин · 0 записей", NotepadFormat.Totals(0, 0));
        Assert.Equal("23 ч 15 мин · 5 записей", NotepadFormat.Totals(1395, 5));
    }

    [Theory]
    [InlineData("06.11.2026", "06.11.2026", "6 нояб.")]
    [InlineData("03.11.2026", "06.11.2026", "3–6 нояб.")]
    [InlineData("30.11.2026", "02.12.2026", "30 нояб. – 2 дек.")]
    [InlineData("29.12.2025", "04.01.2026", "29 дек. 2025 – 4 янв. 2026")]
    public void DateSpanShort(string start, string end, string expected) => Assert.Equal(expected, RuFormat.DateSpanShort(D(start), D(end)));

    [Fact]
    public void DayMonthTime() => Assert.Equal("5 мая, 07:05", RuFormat.DayMonthTime(new DateTime(2026, 5, 5, 7, 5, 59)));
}
