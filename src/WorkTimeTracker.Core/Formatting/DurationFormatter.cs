namespace WorkTimeTracker.Core.Formatting;

/// <summary>Форматирование длительностей по правилам F7.7.</summary>
public static class DurationFormatter
{
    /// <summary>Полный формат: «8 ч 05 мин». Отрицательные значения — с минусом: «−1 ч 30 мин».</summary>
    public static string Format(int totalMinutes)
    {
        var sign = totalMinutes < 0 ? "−" : string.Empty;
        var abs = Math.Abs((long)totalMinutes);
        return $"{sign}{abs / 60} ч {abs % 60:00} мин";
    }

    /// <inheritdoc cref="Format(int)"/>
    public static string Format(TimeSpan duration) => Format(ToMinutes(duration));

    /// <summary>Компактный формат для крупных KPI: «8:05», «124:30».</summary>
    public static string FormatCompact(int totalMinutes)
    {
        var sign = totalMinutes < 0 ? "−" : string.Empty;
        var abs = Math.Abs((long)totalMinutes);
        return $"{sign}{abs / 60}:{abs % 60:00}";
    }

    /// <summary>Короткий формат: целые часы — «40 ч», иначе — как <see cref="Format(int)"/>.</summary>
    public static string FormatShort(int totalMinutes) =>
        totalMinutes % 60 == 0 ? $"{(totalMinutes < 0 ? "−" : string.Empty)}{Math.Abs((long)totalMinutes) / 60} ч" : Format(totalMinutes);

    /// <summary>Со знаком для разниц: «+2 ч 15 мин», «−0 ч 45 мин», «0 ч 00 мин».</summary>
    public static string FormatSigned(int totalMinutes) =>
        totalMinutes > 0 ? "+" + Format(totalMinutes) : Format(totalMinutes);

    /// <summary>Округляет <see cref="TimeSpan"/> до целых минут (математическое округление).</summary>
    public static int ToMinutes(TimeSpan duration) => (int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero);
}
