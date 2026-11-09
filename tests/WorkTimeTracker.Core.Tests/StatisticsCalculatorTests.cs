using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Statistics;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F3.2: показатели на фиксированных наборах данных. «Сегодня» — вторник 10.11.2026.</summary>
public class StatisticsCalculatorTests
{
    private static readonly AppSettings Settings = new();
    private static long _id;

    private static WorkEntry E(string date, int start, int end) => new(++_id, D(date), start, end, "");

    /// <summary>Прошлая неделя 02.11–08.11 + одна запись в позапрошлой + текущая неделя.</summary>
    private static readonly WorkEntry[] Data =
    [
        E("27.10.2026", 540, 1020),   // позапрошлая неделя: 8 ч
        E("02.11.2026", 540, 780),    // Пн 4 ч
        E("02.11.2026", 840, 1080),   // Пн 4 ч
        E("03.11.2026", 600, 1140),   // Вт 9 ч
        E("05.11.2026", 480, 720),    // Чт 4 ч
        E("06.11.2026", 1320, 1560),  // Пт ночная смена 22:00–02:00, 4 ч
        E("07.11.2026", 600, 660),    // Сб 1 ч
        E("08.11.2026", 1380, 1500),  // Вс 23:00–01:00 — ночная смена на границе недель, 2 ч
        E("09.11.2026", 540, 1080),   // Пн 9 ч (текущая неделя)
        E("10.11.2026", 540, 720),    // Вт 3 ч (сегодня)
    ];

    private static PeriodStatistics Week(string anyDate) =>
        StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Week, D(anyDate)), Data, Today, Settings);

    [Fact]
    public void FullWeek_AllIndicators()
    {
        var s = Week("04.11.2026");

        Assert.Equal(1680, s.TotalMinutes);               // 480 + 540 + 240 + 240 + 60 + 120
        Assert.Equal(7, s.EntryCount);
        Assert.Equal(6, s.WorkDays);
        Assert.Equal(7, s.CalendarDays);
        Assert.Equal(280, s.AveragePerWorkDay);
        Assert.Equal(240, s.AveragePerCalendarDay);
        Assert.Equal(240, s.AverageEntryMinutes);
        Assert.Equal(new DayTotal(D("03.11.2026"), 540), s.LongestDay);
        Assert.Equal(new DayTotal(D("07.11.2026"), 60), s.ShortestDay);
        Assert.Equal((540 + 600 + 480 + 1320 + 600 + 1380) / 6.0, s.AverageStartMinute);
        Assert.Equal((1080 + 1140 + 720 + 1560 + 660 + 1500) / 6.0, s.AverageEndMinute);
        Assert.Equal(5, s.NormDays);
        Assert.Equal(2400, s.NormMinutes);
        Assert.Equal(-720, s.BalanceMinutes);
        Assert.Equal(D("08.11.2026"), s.EffectiveEnd);
    }

    [Fact]
    public void FullWeek_ComparedWithPreviousWeek()
    {
        var c = Week("04.11.2026").Comparison!;
        Assert.Equal(D("26.10.2026"), c.Previous.Start);
        Assert.Equal(480, c.PreviousTotalMinutes);
        Assert.Equal(1200, c.DeltaMinutes);
        Assert.Equal(250, c.DeltaPercent);
    }

    [Fact]
    public void CurrentWeek_CountsDaysUntilTodayOnly()
    {
        var s = Week("10.11.2026");

        Assert.Equal(720, s.TotalMinutes);
        Assert.Equal(2, s.CalendarDays);          // Пн и Вт
        Assert.Equal(360, s.AveragePerCalendarDay);
        Assert.Equal(2, s.NormDays);
        Assert.Equal(960, s.NormMinutes);
        Assert.Equal(-240, s.BalanceMinutes);
        Assert.Equal(Today, s.EffectiveEnd);
        Assert.Equal(7, s.Daily.Count);           // график — по всем дням недели
        Assert.Equal(-960, s.Comparison!.DeltaMinutes);
    }

    [Fact]
    public void NightShift_OnWeekBoundary_BelongsToStartDate()
    {
        var current = Week("10.11.2026");
        Assert.DoesNotContain(current.Daily, d => d.Date == D("08.11.2026"));
        Assert.Equal(0, current.Daily.Single(d => d.Date == D("15.11.2026")).Minutes);
        Assert.Equal(120, Week("04.11.2026").Daily.Single(d => d.Date == D("08.11.2026")).Minutes);
    }

    [Fact]
    public void EmptyPeriod()
    {
        var s = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Month, D("15.06.2026")), Data, Today, Settings);

        Assert.Equal(0, s.TotalMinutes);
        Assert.Equal(0, s.EntryCount);
        Assert.Equal(0, s.WorkDays);
        Assert.Equal(30, s.CalendarDays);
        Assert.Equal(0, s.AveragePerWorkDay);
        Assert.Equal(0, s.AveragePerCalendarDay);
        Assert.Equal(0, s.AverageEntryMinutes);
        Assert.Null(s.LongestDay);
        Assert.Null(s.ShortestDay);
        Assert.Null(s.AverageStartMinute);
        Assert.Null(s.AverageEndMinute);
        Assert.Equal(22, s.NormDays);             // будни июня 2026
        Assert.Equal(0, s.Comparison!.PreviousTotalMinutes);
        Assert.Null(s.Comparison.DeltaPercent);
    }

    [Fact]
    public void FuturePeriod_HasNoCalendarDays()
    {
        var s = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Week, D("20.11.2026")), Data, Today, Settings);
        Assert.Equal(0, s.CalendarDays);
        Assert.Equal(0, s.NormDays);
        Assert.Equal(0, s.AveragePerCalendarDay);
    }

    [Fact]
    public void Day()
    {
        var s = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Day, D("02.11.2026")), Data, Today, Settings);
        Assert.Equal(480, s.TotalMinutes);
        Assert.Equal(2, s.EntryCount);
        Assert.Equal(1, s.CalendarDays);
        Assert.Equal(480, s.NormMinutes);
        Assert.Equal(0, s.BalanceMinutes);
        Assert.Equal(D("01.11.2026"), s.Comparison!.Previous.Start);
    }

    [Fact]
    public void Norm_UsesConfiguredWorkDaysAndDailyNorm()
    {
        var settings = new AppSettings
        {
            DailyNormMinutes = 420,
            WorkDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday },
        };
        var s = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Week, D("04.11.2026")), Data, Today, settings);
        Assert.Equal(6, s.NormDays);
        Assert.Equal(2520, s.NormMinutes);
        Assert.Equal(1680 - 2520, s.BalanceMinutes);
    }

    [Fact]
    public void Year_MonthlySeries()
    {
        var s = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Year, Today), Data, Today, Settings);
        Assert.Equal(12, s.Monthly.Count);
        Assert.Equal(480, s.Monthly[9].Minutes);   // октябрь
        Assert.Equal(1680 + 720, s.Monthly[10].Minutes); // ноябрь
        Assert.Equal(0, s.Monthly[11].Minutes);
        Assert.Equal(314, s.CalendarDays);         // 01.01–10.11.2026
        Assert.Equal(365, s.Daily.Count);
        Assert.Equal(D("01.01.2025"), s.Comparison!.Previous.Start);
    }

    [Fact]
    public void AllTime_FromFirstEntry_NoComparison()
    {
        var period = PeriodNavigator.ForDate(PeriodKind.AllTime, Today, D("27.10.2026"));
        var s = StatisticsCalculator.Calculate(period, Data, Today, Settings);
        Assert.Equal(480 + 1680 + 720, s.TotalMinutes);
        Assert.Equal(10, s.EntryCount);
        Assert.Equal(15, s.CalendarDays);
        Assert.Null(s.Comparison);
        Assert.Equal([(2026, 10), (2026, 11)], s.Monthly.Select(m => (m.Year, m.Month)));
    }

    [Fact]
    public void Custom_ComparedWithSameLengthBefore()
    {
        var s = StatisticsCalculator.Calculate(PeriodNavigator.Custom(D("02.11.2026"), D("08.11.2026")), Data, Today, Settings);
        Assert.Equal(1680, s.TotalMinutes);
        Assert.Equal(D("26.10.2026"), s.Comparison!.Previous.Start);
        Assert.Equal(D("01.11.2026"), s.Comparison.Previous.End);
    }

    [Fact]
    public void WeekdayAverages_MondayFirst()
    {
        var s = Week("04.11.2026");
        Assert.Equal(DayOfWeek.Monday, s.Weekdays[0].Day);
        Assert.Equal(DayOfWeek.Sunday, s.Weekdays[6].Day);
        Assert.Equal(480, s.Weekdays[0].AverageMinutes);
        Assert.Equal(0, s.Weekdays[2].AverageMinutes);   // среда без записей
        Assert.All(s.Weekdays, w => Assert.Equal(1, w.Occurrences));

        var month = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Month, Today), Data, Today, Settings);
        var monday = month.Weekdays[0];
        Assert.Equal(2, monday.Occurrences);             // 02.11 и 09.11
        Assert.Equal((480 + 540) / 2.0, monday.AverageMinutes);
        Assert.Equal(1, month.Weekdays[2].Occurrences); // среды до 10.11: только 04.11
    }

    [Fact]
    public void FuturePeriod_WeekdaysHaveNoOccurrences()
    {
        var s = StatisticsCalculator.Calculate(PeriodNavigator.ForDate(PeriodKind.Week, D("20.11.2026")), Data, Today, Settings);
        Assert.All(s.Weekdays, w => Assert.Equal(0, w.AverageMinutes));
    }

    [Fact]
    public void Total_ForSummary() =>
        Assert.Equal(720, StatisticsCalculator.Total(PeriodNavigator.ForDate(PeriodKind.Week, Today), Data));

    [Theory]
    [InlineData(1032.4, "17:12")]
    [InlineData(1500.0, "01:00 +1")]
    [InlineData(1440.0, "24:00")]
    [InlineData(null, "—")]
    public void FormatAverageClock(double? minute, string expected) => Assert.Equal(expected, StatisticsCalculator.FormatAverageClock(minute));
}
