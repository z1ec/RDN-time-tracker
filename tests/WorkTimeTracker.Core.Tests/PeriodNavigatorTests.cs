using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Statistics;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F3.1: периоды и навигация.</summary>
public class PeriodNavigatorTests
{
    [Theory]
    [InlineData("10.11.2026", "09.11.2026")] // вторник → понедельник
    [InlineData("09.11.2026", "09.11.2026")] // понедельник
    [InlineData("15.11.2026", "09.11.2026")] // воскресенье
    [InlineData("01.01.2026", "29.12.2025")] // через границу года
    public void StartOfWeek_IsMonday(string date, string monday) => Assert.Equal(D(monday), PeriodNavigator.StartOfWeek(D(date)));

    [Theory]
    [InlineData(PeriodKind.Day, "10.11.2026", "10.11.2026")]
    [InlineData(PeriodKind.Week, "09.11.2026", "15.11.2026")]
    [InlineData(PeriodKind.Month, "01.11.2026", "30.11.2026")]
    [InlineData(PeriodKind.Year, "01.01.2026", "31.12.2026")]
    [InlineData(PeriodKind.Custom, "10.11.2026", "10.11.2026")]
    public void ForDate(PeriodKind kind, string start, string end)
    {
        var p = PeriodNavigator.ForDate(kind, Today);
        Assert.Equal(kind, p.Kind);
        Assert.Equal(D(start), p.Start);
        Assert.Equal(D(end), p.End);
    }

    [Fact]
    public void AllTime_StartsAtFirstEntry()
    {
        var p = PeriodNavigator.ForDate(PeriodKind.AllTime, Today, D("01.03.2025"));
        Assert.Equal(D("01.03.2025"), p.Start);
        Assert.Equal(Today, p.End);
        Assert.Equal(Today, PeriodNavigator.ForDate(PeriodKind.AllTime, Today).Start);
    }

    [Fact]
    public void Month_February_LeapYear() => Assert.Equal(D("29.02.2024"), PeriodNavigator.ForDate(PeriodKind.Month, D("10.02.2024")).End);

    [Theory]
    [InlineData(PeriodKind.Day, "09.11.2026", "09.11.2026")]
    [InlineData(PeriodKind.Week, "02.11.2026", "08.11.2026")]
    [InlineData(PeriodKind.Month, "01.10.2026", "31.10.2026")]
    [InlineData(PeriodKind.Year, "01.01.2025", "31.12.2025")]
    public void Previous(PeriodKind kind, string start, string end)
    {
        var prev = PeriodNavigator.Previous(PeriodNavigator.ForDate(kind, Today))!;
        Assert.Equal(D(start), prev.Start);
        Assert.Equal(D(end), prev.End);
    }

    [Fact]
    public void Previous_Custom_IsSameLengthRightBefore()
    {
        var prev = PeriodNavigator.Previous(PeriodNavigator.Custom(D("10.11.2026"), D("01.11.2026")))!;
        Assert.Equal(D("22.10.2026"), prev.Start);
        Assert.Equal(D("31.10.2026"), prev.End);
    }

    [Fact]
    public void Previous_AllTime_IsNull() => Assert.Null(PeriodNavigator.Previous(PeriodNavigator.ForDate(PeriodKind.AllTime, Today)));

    [Fact]
    public void Shift_Month_FromJanuary31()
    {
        var jan = PeriodNavigator.ForDate(PeriodKind.Month, D("31.01.2026"));
        var feb = PeriodNavigator.Shift(jan, 1);
        Assert.Equal(D("01.02.2026"), feb.Start);
        Assert.Equal(D("28.02.2026"), feb.End);
    }

    [Fact]
    public void Shift_AllTime_DoesNothing()
    {
        var all = PeriodNavigator.ForDate(PeriodKind.AllTime, Today, D("01.01.2026"));
        Assert.Same(all, PeriodNavigator.Shift(all, 1));
    }

    [Fact]
    public void CanGoNext_NotIntoFuture()
    {
        Assert.False(PeriodNavigator.CanGoNext(PeriodNavigator.ForDate(PeriodKind.Week, Today), Today));
        Assert.True(PeriodNavigator.CanGoNext(PeriodNavigator.ForDate(PeriodKind.Week, D("01.11.2026")), Today));
        Assert.False(PeriodNavigator.CanGoNext(PeriodNavigator.ForDate(PeriodKind.AllTime, Today), Today));
    }

    [Fact]
    public void IsCurrent_And_EffectiveEnd()
    {
        var week = PeriodNavigator.ForDate(PeriodKind.Week, Today);
        Assert.True(PeriodNavigator.IsCurrent(week, Today));
        Assert.Equal(Today, PeriodNavigator.EffectiveEnd(week, Today));
        var past = PeriodNavigator.Shift(week, -1);
        Assert.False(PeriodNavigator.IsCurrent(past, Today));
        Assert.Equal(past.End, PeriodNavigator.EffectiveEnd(past, Today));
    }

    [Fact]
    public void Period_DaysAndContains()
    {
        var p = new Period(PeriodKind.Custom, D("30.11.2026"), D("02.12.2026"));
        Assert.Equal(3, p.DayCount);
        Assert.Equal([D("30.11.2026"), D("01.12.2026"), D("02.12.2026")], p.Days());
        Assert.True(p.Contains(D("01.12.2026")));
        Assert.False(p.Contains(D("03.12.2026")));
    }

    [Fact]
    public void UnknownKind_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PeriodNavigator.ForDate((PeriodKind)99, Today));
        Assert.Throws<ArgumentOutOfRangeException>(() => PeriodNavigator.Shift(new Period((PeriodKind)99, Today, Today), 1));
    }
}
