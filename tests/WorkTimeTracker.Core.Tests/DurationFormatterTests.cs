using WorkTimeTracker.Core.Formatting;

namespace WorkTimeTracker.Core.Tests;

public class DurationFormatterTests
{
    [Theory]
    [InlineData(0, "0 ч 00 мин")]
    [InlineData(5, "0 ч 05 мин")]
    [InlineData(485, "8 ч 05 мин")]
    [InlineData(540, "9 ч 00 мин")]
    [InlineData(1440, "24 ч 00 мин")]
    [InlineData(-90, "−1 ч 30 мин")]
    public void Format_Minutes(int minutes, string expected) => Assert.Equal(expected, DurationFormatter.Format(minutes));

    [Fact]
    public void Format_TimeSpan_RoundsToMinutes() =>
        Assert.Equal("1 ч 01 мин", DurationFormatter.Format(TimeSpan.FromSeconds(60 * 60 + 30)));

    [Theory]
    [InlineData(485, "8:05")]
    [InlineData(7470, "124:30")]
    [InlineData(-30, "−0:30")]
    public void FormatCompact(int minutes, string expected) => Assert.Equal(expected, DurationFormatter.FormatCompact(minutes));

    [Theory]
    [InlineData(2400, "40 ч")]
    [InlineData(-120, "−2 ч")]
    [InlineData(1870, "31 ч 10 мин")]
    public void FormatShort(int minutes, string expected) => Assert.Equal(expected, DurationFormatter.FormatShort(minutes));

    [Theory]
    [InlineData(135, "+2 ч 15 мин")]
    [InlineData(-45, "−0 ч 45 мин")]
    [InlineData(0, "0 ч 00 мин")]
    public void FormatSigned(int minutes, string expected) => Assert.Equal(expected, DurationFormatter.FormatSigned(minutes));
}
