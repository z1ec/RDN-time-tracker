using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Statistics;

/// <summary>Построение периодов статистики и навигация ◀ / ▶ (F3.1, F3.2).</summary>
public static class PeriodNavigator
{
    /// <summary>Понедельник недели, в которую входит дата (неделя Пн–Вс).</summary>
    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Период вида <paramref name="kind"/>, содержащий дату.</summary>
    /// <param name="kind">Вид периода; для <see cref="PeriodKind.Custom"/> используйте <see cref="Custom"/>.</param>
    /// <param name="date">Опорная дата.</param>
    /// <param name="firstEntryDate">Дата первой записи — начало «Всего времени» (если записей нет — опорная дата).</param>
    public static Period ForDate(PeriodKind kind, DateOnly date, DateOnly? firstEntryDate = null) => kind switch
    {
        PeriodKind.Day => new Period(kind, date, date),
        PeriodKind.Week => new Period(kind, StartOfWeek(date), StartOfWeek(date).AddDays(6)),
        PeriodKind.Month => new Period(kind, new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))),
        PeriodKind.Year => new Period(kind, new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31)),
        PeriodKind.AllTime => new Period(kind, Min(firstEntryDate ?? date, date), date),
        PeriodKind.Custom => new Period(kind, date, date),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Произвольный период; даты упорядочиваются автоматически.</summary>
    public static Period Custom(DateOnly a, DateOnly b) => new(PeriodKind.Custom, Min(a, b), Max(a, b));

    /// <summary>Сдвиг периода на <paramref name="steps"/> шагов (−1 — ◀, +1 — ▶). «Всё время» не сдвигается.</summary>
    public static Period Shift(Period p, int steps) => p.Kind switch
    {
        PeriodKind.Day => new Period(p.Kind, p.Start.AddDays(steps), p.End.AddDays(steps)),
        PeriodKind.Week => new Period(p.Kind, p.Start.AddDays(7 * steps), p.End.AddDays(7 * steps)),
        PeriodKind.Month => ForDate(PeriodKind.Month, p.Start.AddMonths(steps)),
        PeriodKind.Year => ForDate(PeriodKind.Year, p.Start.AddYears(steps)),
        PeriodKind.Custom => new Period(p.Kind, p.Start.AddDays(p.DayCount * steps), p.End.AddDays(p.DayCount * steps)),
        PeriodKind.AllTime => p,
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    /// <summary>
    /// Период для сравнения (F3.2): предыдущий день / неделя / месяц / год; для произвольного —
    /// отрезок той же длины прямо перед ним; для «Всего времени» — <c>null</c>.
    /// </summary>
    public static Period? Previous(Period p) => p.Kind == PeriodKind.AllTime ? null : Shift(p, -1);

    /// <summary>Можно ли перейти вперёд (▶): следующий период не должен начинаться в будущем.</summary>
    public static bool CanGoNext(Period p, DateOnly today) => p.Kind != PeriodKind.AllTime && Shift(p, 1).Start <= today;

    /// <summary>Содержит ли период сегодняшнюю дату («текущий период»).</summary>
    public static bool IsCurrent(Period p, DateOnly today) => p.Contains(today);

    /// <summary>
    /// Последний учитываемый день для средних и нормы: для текущего периода — сегодня, иначе конец периода.
    /// Для периода целиком в будущем результат меньше начала (дней — 0).
    /// </summary>
    public static DateOnly EffectiveEnd(Period p, DateOnly today) => Min(p.End, today);

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
}
