using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Statistics;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F7.7: русские форматы независимо от локали ОС.</summary>
public class RuFormatTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(545, "09:05")]
    [InlineData(1440, "24:00")]
    [InlineData(1560, "02:00")]
    public void Clock(int minute, string expected) => Assert.Equal(expected, RuFormat.Clock(minute));

    [Theory]
    [InlineData(1080, "18:00")]
    [InlineData(1440, "24:00")]
    [InlineData(1560, "02:00 +1")]
    public void ClockWithDayOffset(int minute, string expected) => Assert.Equal(expected, RuFormat.ClockWithDayOffset(minute));

    [Fact]
    public void Range() => Assert.Equal("22:00 – 02:00 +1", RuFormat.Range(new TimeRange(1320, 1560)));

    [Fact]
    public void Dates()
    {
        Assert.Equal("10.11", RuFormat.DayMonthNumeric(Today));
        Assert.Equal("10.11.2026", RuFormat.DateNumeric(Today));
        Assert.Equal("10 нояб.", RuFormat.DayMonthShort(Today));
        Assert.Equal("10 ноября", RuFormat.DayMonth(Today));
        Assert.Equal("10 ноября 2026", RuFormat.DayMonthYear(Today));
        Assert.Equal("Пн, 9 ноября", RuFormat.GroupHeader(D("09.11.2026"), Today));
        Assert.Equal("Ср, 31 декабря 2025", RuFormat.GroupHeader(D("31.12.2025"), Today));
        Assert.Equal("Ноябрь", RuFormat.MonthNominative(11));
        Assert.Equal("ноя", RuFormat.MonthShortNominative(11));
        Assert.Equal("мая", RuFormat.MonthGenitive(5));
        Assert.Equal("Вс", RuFormat.DayShort(DayOfWeek.Sunday));
        Assert.Equal("среда", RuFormat.DayFull(DayOfWeek.Wednesday));
        Assert.Equal("", RuFormat.Capitalize(""));
    }

    [Theory]
    [InlineData(PeriodKind.Day, "10.11.2026", "10.11.2026", "Вторник, 10 ноября 2026")]
    [InlineData(PeriodKind.Week, "09.11.2026", "15.11.2026", "Неделя 9–15 ноября 2026")]
    [InlineData(PeriodKind.Week, "30.11.2026", "06.12.2026", "Неделя 30 ноября – 6 декабря 2026")]
    [InlineData(PeriodKind.Week, "29.12.2025", "04.01.2026", "Неделя 29 декабря 2025 – 4 января 2026")]
    [InlineData(PeriodKind.Month, "01.11.2026", "30.11.2026", "Ноябрь 2026")]
    [InlineData(PeriodKind.Year, "01.01.2026", "31.12.2026", "2026 год")]
    [InlineData(PeriodKind.AllTime, "01.03.2025", "10.11.2026", "Всё время · 1 марта 2025 – 10 ноября 2026")]
    [InlineData(PeriodKind.Custom, "01.11.2026", "05.11.2026", "1–5 ноября 2026")]
    [InlineData(PeriodKind.Custom, "05.11.2026", "05.11.2026", "5 ноября 2026")]
    public void PeriodLabel(PeriodKind kind, string start, string end, string expected) =>
        Assert.Equal(expected, RuFormat.PeriodLabel(new Period(kind, D(start), D(end))));

    [Fact]
    public void PeriodLabel_UnknownKind_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RuFormat.PeriodLabel(new Period((PeriodKind)42, Today, Today)));

    [Fact]
    public void DateSpan_SameDay() => Assert.Equal("10 ноября 2026", RuFormat.DateSpan(Today, Today));

    [Fact]
    public void Culture_IsRussian_WithFixedNames()
    {
        Assert.Equal("ru-RU", RuFormat.Culture.Name);
        Assert.Equal(DayOfWeek.Monday, RuFormat.Culture.DateTimeFormat.FirstDayOfWeek);
        Assert.Equal("ноября", RuFormat.Culture.DateTimeFormat.MonthGenitiveNames[10]);
        Assert.Equal("1,50", 1.5.ToString("0.00", RuFormat.Culture));
        Assert.Equal(7, RuFormat.WeekDaysMondayFirst.Count);
        _ = PeriodNavigator.StartOfWeek(Today);
    }
}
