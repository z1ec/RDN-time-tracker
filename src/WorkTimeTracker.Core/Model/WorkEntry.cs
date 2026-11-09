namespace WorkTimeTracker.Core.Model;

/// <summary>
/// Запись о работе. Время — локальное «настенное», в минутах от начала <see cref="Date"/> (ADR-0003).
/// Ночная смена целиком относится к дате начала (F3.6).
/// </summary>
public sealed record WorkEntry(long Id, DateOnly Date, int StartMinute, int EndMinute, string Comment)
{
    public WorkEntry(long id, DateOnly date, TimeRange range, string comment)
        : this(id, date, range.StartMinute, range.EndMinute, comment)
    {
    }

    public TimeRange Range => new(StartMinute, EndMinute);

    public TimeOnly Start => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(StartMinute));

    /// <summary>«Настенное» время окончания (для 24:00 и ночных смен — время следующих суток).</summary>
    public TimeOnly End => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(EndMinute % TimeRange.MinutesPerDay));

    public bool EndsNextDay => EndMinute > TimeRange.MinutesPerDay;

    public int DurationMinutes => EndMinute - StartMinute;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>Начало в абсолютных минутах (от 01.01.0001) — для проверки пересечений между днями.</summary>
    public long AbsoluteStart => ToAbsolute(Date, StartMinute);

    public long AbsoluteEnd => ToAbsolute(Date, EndMinute);

    public static long ToAbsolute(DateOnly date, int minute) => (date.DayNumber * (long)TimeRange.MinutesPerDay) + minute;
}

/// <summary>Данные для создания или изменения записи (без Id).</summary>
public sealed record EntryDraft(DateOnly Date, TimeRange Range, string Comment)
{
    public WorkEntry ToEntry(long id) => new(id, Date, Range, Comment);

    public long AbsoluteStart => WorkEntry.ToAbsolute(Date, Range.StartMinute);

    public long AbsoluteEnd => WorkEntry.ToAbsolute(Date, Range.EndMinute);
}
