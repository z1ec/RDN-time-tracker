namespace WorkTimeTracker.Core.Model;

/// <summary>Вид периода статистики (F3.1).</summary>
public enum PeriodKind
{
    Day,
    Week,
    Month,
    Year,
    AllTime,
    Custom,
}

/// <summary>Период статистики: даты начала и конца включительно.</summary>
public sealed record Period(PeriodKind Kind, DateOnly Start, DateOnly End)
{
    public int DayCount => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public IEnumerable<DateOnly> Days()
    {
        for (var d = Start; d <= End; d = d.AddDays(1))
        {
            yield return d;
        }
    }
}
