using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F8.6, F8.15, F8.16: тексты плашки и карточек архива.</summary>
public class NotepadFormatTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 3, 6, 12, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.CreateCustomTimeZone("msk", TimeSpan.FromHours(3), "msk", "msk");

    private static NotepadSheetView View(string title, DateTimeOffset? cleared, params WorkEntry[] entries) =>
        new(new NotepadSheet(1, title, Started, cleared), entries);

    private static WorkEntry E(string date, int start, int end) => new(1, D(date), start, end, "");

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
