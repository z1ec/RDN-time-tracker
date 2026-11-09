namespace WorkTimeTracker.Core.Model;

/// <summary>
/// Интервал времени внутри записи в минутах от начала даты записи (ADR-0003).
/// <para><see cref="StartMinute"/> — 0…1439; <see cref="EndMinute"/> — больше начала, может быть &gt; 1440 (ночная смена).</para>
/// </summary>
public readonly record struct TimeRange
{
    public const int MinutesPerDay = 1440;

    public TimeRange(int startMinute, int endMinute)
    {
        if (startMinute is < 0 or >= MinutesPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(startMinute), startMinute, "Начало должно быть в диапазоне 0…1439 минут");
        }

        if (endMinute <= startMinute || endMinute - startMinute > MinutesPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(endMinute), endMinute, "Длительность должна быть больше 0 и не больше 24 ч");
        }

        StartMinute = startMinute;
        EndMinute = endMinute;
    }

    public int StartMinute { get; }

    public int EndMinute { get; }

    public int DurationMinutes => EndMinute - StartMinute;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>Конец приходится на следующие сутки (строго после 24:00).</summary>
    public bool EndsNextDay => EndMinute > MinutesPerDay;

    /// <summary>
    /// Строит интервал из «настенных» времён начала (0…1439) и конца (0…1440).
    /// Конец раньше начала — ночная смена: конец переносится на следующие сутки.
    /// Возвращает <c>null</c>, если длительность нулевая.
    /// </summary>
    public static TimeRange? FromClock(int startMinute, int endClockMinute)
    {
        if (startMinute is < 0 or >= MinutesPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(startMinute));
        }

        if (endClockMinute is < 0 or > MinutesPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(endClockMinute));
        }

        if (endClockMinute == startMinute)
        {
            return null;
        }

        var end = endClockMinute > startMinute ? endClockMinute : endClockMinute + MinutesPerDay;
        return new TimeRange(startMinute, end);
    }
}
