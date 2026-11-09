using Microsoft.Extensions.Time.Testing;

namespace WorkTimeTracker.Core.Tests;

/// <summary>Фиксированные часы для тестов: «сегодня» — 10.11.2026 (вторник), 14:37.</summary>
public static class TestClock
{
    public static readonly DateOnly Today = new(2026, 11, 10);

    public static FakeTimeProvider Create(int year = 2026, int month = 11, int day = 10, int hour = 14, int minute = 37)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    public static DateOnly D(string ddMMyyyy) => DateOnly.ParseExact(ddMMyyyy, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
