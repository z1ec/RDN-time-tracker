using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Statistics;

/// <summary>Сумма минут за день.</summary>
public sealed record DayTotal(DateOnly Date, int Minutes);

/// <summary>Сумма минут за месяц.</summary>
public sealed record MonthTotal(int Year, int Month, int Minutes);

/// <summary>Среднее за день недели: сумма за все такие дни / число таких календарных дней в периоде.</summary>
public sealed record WeekdayAverage(DayOfWeek Day, double AverageMinutes, int Occurrences);

/// <summary>Сравнение с предыдущим периодом (F3.2, P1).</summary>
/// <param name="Previous">Период сравнения.</param>
/// <param name="PreviousTotalMinutes">Итого за период сравнения.</param>
/// <param name="DeltaMinutes">Разница: текущий − предыдущий.</param>
/// <param name="DeltaPercent">Разница в процентах; <c>null</c>, если в предыдущем периоде 0 минут.</param>
public sealed record PeriodComparison(Period Previous, int PreviousTotalMinutes, int DeltaMinutes, double? DeltaPercent);

/// <summary>Все показатели периода. Точные формулы — в <c>docs/STATISTICS.md</c>.</summary>
public sealed record PeriodStatistics
{
    public required Period Period { get; init; }

    /// <summary>Учитываемый конец периода: для текущего — сегодня (<see cref="PeriodNavigator.EffectiveEnd"/>).</summary>
    public required DateOnly EffectiveEnd { get; init; }

    public required int TotalMinutes { get; init; }

    public required int EntryCount { get; init; }

    /// <summary>Рабочие дни — дни хотя бы с одной записью.</summary>
    public required int WorkDays { get; init; }

    /// <summary>Календарных дней в периоде (для текущего — до сегодня включительно).</summary>
    public required int CalendarDays { get; init; }

    public required double AveragePerWorkDay { get; init; }

    public required double AveragePerCalendarDay { get; init; }

    public required double AverageEntryMinutes { get; init; }

    public required DayTotal? LongestDay { get; init; }

    public required DayTotal? ShortestDay { get; init; }

    /// <summary>Среднее время начала (по первой записи дня), минуты от полуночи.</summary>
    public required double? AverageStartMinute { get; init; }

    /// <summary>Среднее время окончания (по последней записи дня); больше 1440 — после полуночи.</summary>
    public required double? AverageEndMinute { get; init; }

    /// <summary>Число рабочих (по настройкам) дней недели в учитываемой части периода.</summary>
    public required int NormDays { get; init; }

    public required int NormMinutes { get; init; }

    /// <summary>Переработка (&gt; 0) или недоработка (&lt; 0): итого − норма.</summary>
    public int BalanceMinutes => TotalMinutes - NormMinutes;

    public required PeriodComparison? Comparison { get; init; }

    /// <summary>Сумма по каждому дню периода (все дни от начала до конца, включая дни без записей).</summary>
    public required IReadOnlyList<DayTotal> Daily { get; init; }

    /// <summary>Сумма по каждому месяцу периода.</summary>
    public required IReadOnlyList<MonthTotal> Monthly { get; init; }

    /// <summary>Средние по дням недели в порядке Пн…Вс.</summary>
    public required IReadOnlyList<WeekdayAverage> Weekdays { get; init; }
}
