using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Statistics;

/// <summary>
/// Расчёт показателей периода (F3.2). Чистая функция: никаких обращений к БД и часам.
/// Ночная смена целиком относится к дате начала (F3.6).
/// </summary>
public static class StatisticsCalculator
{
    /// <summary>Считает статистику периода.</summary>
    /// <param name="period">Период.</param>
    /// <param name="entries">
    /// Записи, покрывающие период и (для сравнения) предыдущий период. Лишние записи отфильтровываются.
    /// </param>
    /// <param name="today">Сегодняшняя дата.</param>
    /// <param name="settings">Норма и рабочие дни недели.</param>
    public static PeriodStatistics Calculate(Period period, IEnumerable<WorkEntry> entries, DateOnly today, AppSettings settings)
    {
        var all = entries as IReadOnlyCollection<WorkEntry> ?? [.. entries];
        var inPeriod = all.Where(e => period.Contains(e.Date)).ToList();

        var effectiveEnd = PeriodNavigator.EffectiveEnd(period, today);
        var calendarDays = Math.Max(0, effectiveEnd.DayNumber - period.Start.DayNumber + 1);

        var total = inPeriod.Sum(e => e.DurationMinutes);
        var byDay = inPeriod
            .GroupBy(e => e.Date)
            .Select(g => new
            {
                Date = g.Key,
                Minutes = g.Sum(e => e.DurationMinutes),
                FirstStart = g.Min(e => e.StartMinute),
                LastEnd = g.Max(e => e.EndMinute),
            })
            .OrderBy(d => d.Date)
            .ToList();
        var workDays = byDay.Count;

        DayTotal? longest = null;
        DayTotal? shortest = null;
        foreach (var d in byDay)
        {
            if (longest is null || d.Minutes > longest.Minutes)
            {
                longest = new DayTotal(d.Date, d.Minutes);
            }

            if (shortest is null || d.Minutes < shortest.Minutes)
            {
                shortest = new DayTotal(d.Date, d.Minutes);
            }
        }

        var normDays = 0;
        for (var d = period.Start; d <= effectiveEnd; d = d.AddDays(1))
        {
            if (settings.WorkDays.Contains(d.DayOfWeek))
            {
                normDays++;
            }
        }

        return new PeriodStatistics
        {
            Period = period,
            EffectiveEnd = effectiveEnd,
            TotalMinutes = total,
            EntryCount = inPeriod.Count,
            WorkDays = workDays,
            CalendarDays = calendarDays,
            AveragePerWorkDay = workDays == 0 ? 0 : (double)total / workDays,
            AveragePerCalendarDay = calendarDays == 0 ? 0 : (double)total / calendarDays,
            AverageEntryMinutes = inPeriod.Count == 0 ? 0 : (double)total / inPeriod.Count,
            LongestDay = longest,
            ShortestDay = shortest,
            AverageStartMinute = workDays == 0 ? null : byDay.Average(d => d.FirstStart),
            AverageEndMinute = workDays == 0 ? null : byDay.Average(d => d.LastEnd),
            NormDays = normDays,
            NormMinutes = normDays * settings.DailyNormMinutes,
            Comparison = Compare(period, all, total),
            Daily = Daily(period, inPeriod),
            Monthly = Monthly(period, inPeriod),
            Weekdays = Weekdays(period.Start, effectiveEnd, inPeriod),
        };
    }

    /// <summary>Итого минут за период (для сводки на главном экране, F3.4).</summary>
    public static int Total(Period period, IEnumerable<WorkEntry> entries) =>
        entries.Where(e => period.Contains(e.Date)).Sum(e => e.DurationMinutes);

    /// <summary>Форматирует среднее время начала/окончания: «09:12» или «02:00 +1».</summary>
    public static string FormatAverageClock(double? minute) =>
        minute is null ? "—" : RuFormat.ClockWithDayOffset((int)Math.Round(minute.Value, MidpointRounding.AwayFromZero));

    private static PeriodComparison? Compare(Period period, IReadOnlyCollection<WorkEntry> all, int total)
    {
        var previous = PeriodNavigator.Previous(period);
        if (previous is null)
        {
            return null;
        }

        var prevTotal = Total(previous, all);
        var delta = total - prevTotal;
        double? percent = prevTotal == 0 ? null : 100.0 * delta / prevTotal;
        return new PeriodComparison(previous, prevTotal, delta, percent);
    }

    private static List<DayTotal> Daily(Period period, List<WorkEntry> inPeriod)
    {
        var sums = inPeriod.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.DurationMinutes));
        return [.. period.Days().Select(d => new DayTotal(d, sums.GetValueOrDefault(d)))];
    }

    private static List<MonthTotal> Monthly(Period period, List<WorkEntry> inPeriod)
    {
        var sums = inPeriod.GroupBy(e => (e.Date.Year, e.Date.Month)).ToDictionary(g => g.Key, g => g.Sum(e => e.DurationMinutes));
        var result = new List<MonthTotal>();
        for (var m = new DateOnly(period.Start.Year, period.Start.Month, 1); m <= period.End; m = m.AddMonths(1))
        {
            result.Add(new MonthTotal(m.Year, m.Month, sums.GetValueOrDefault((m.Year, m.Month))));
        }

        return result;
    }

    private static List<WeekdayAverage> Weekdays(DateOnly start, DateOnly effectiveEnd, List<WorkEntry> inPeriod)
    {
        var occurrences = new int[7];
        for (var d = start; d <= effectiveEnd; d = d.AddDays(1))
        {
            occurrences[(int)d.DayOfWeek]++;
        }

        var sums = new int[7];
        foreach (var e in inPeriod)
        {
            sums[(int)e.Date.DayOfWeek] += e.DurationMinutes;
        }

        return [.. RuFormat.WeekDaysMondayFirst.Select(day =>
            new WeekdayAverage(day, occurrences[(int)day] == 0 ? 0 : (double)sums[(int)day] / occurrences[(int)day], occurrences[(int)day]))];
    }
}
