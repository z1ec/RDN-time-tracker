using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Statistics;

namespace WorkTimeTracker.App.Controls;

/// <summary>Клетка тепловой карты года (F3.3, P2): столбец — неделя, строка — день недели (Пн = 0).</summary>
public sealed record HeatmapCell(DateOnly Date, int Column, int Row, int Level, string Tooltip, bool IsFuture)
{
    public const int Levels = 4;

    /// <summary>Строит клетки по суммам за каждый день года. Уровень 0 — нет работы, 1…4 — квартили от максимума.</summary>
    public static IReadOnlyList<HeatmapCell> Build(IReadOnlyList<DayTotal> days, DateOnly today)
    {
        if (days.Count == 0)
        {
            return [];
        }

        var max = Math.Max(1, days.Max(d => d.Minutes));
        var firstMonday = PeriodNavigator.StartOfWeek(days[0].Date);
        return [.. days.Select(d =>
        {
            var column = (d.Date.DayNumber - firstMonday.DayNumber) / 7;
            var row = ((int)d.Date.DayOfWeek + 6) % 7;
            var level = d.Minutes == 0 ? 0 : Math.Clamp((int)Math.Ceiling(Levels * (double)d.Minutes / max), 1, Levels);
            return new HeatmapCell(d.Date, column, row, level, $"{RuFormat.GroupHeader(d.Date, today)}: {DurationFormatter.Format(d.Minutes)}", d.Date > today);
        })];
    }
}
