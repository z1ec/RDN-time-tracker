using System.Globalization;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Formatting;

/// <summary>
/// Русские форматы дат и времени (F7.7) независимо от локали ОС.
/// Основа — <c>CultureInfo("ru-RU")</c>, но названия месяцев и дней недели заданы явно:
/// так вывод одинаков на Windows (NLS/ICU), macOS и Linux.
/// </summary>
public static class RuFormat
{
    private static readonly string[] MonthsNominative =
        ["январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"];

    private static readonly string[] MonthsGenitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    private static readonly string[] MonthsShort =
        ["янв.", "февр.", "мар.", "апр.", "мая", "июн.", "июл.", "авг.", "сент.", "окт.", "нояб.", "дек."];

    private static readonly string[] MonthsShortNominative =
        ["янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    // Индекс — DayOfWeek (0 = воскресенье).
    private static readonly string[] DaysShort = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];

    private static readonly string[] DaysFull = ["воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота"];

    /// <summary>Культура ru-RU с явно заданными названиями (для DatePicker и форматирования чисел).</summary>
    public static CultureInfo Culture { get; } = CreateCulture();

    /// <summary>Дни недели в порядке Пн…Вс.</summary>
    public static IReadOnlyList<DayOfWeek> WeekDaysMondayFirst { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static CultureInfo CreateCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
        var f = culture.DateTimeFormat;
        f.MonthNames = [.. MonthsNominative.Select(Capitalize), string.Empty];
        f.MonthGenitiveNames = [.. MonthsGenitive, string.Empty];
        f.AbbreviatedMonthNames = [.. MonthsShortNominative, string.Empty];
        f.AbbreviatedMonthGenitiveNames = [.. MonthsShort, string.Empty];
        f.DayNames = [.. DaysFull];
        f.AbbreviatedDayNames = [.. DaysShort];
        f.ShortestDayNames = [.. DaysShort];
        f.FirstDayOfWeek = DayOfWeek.Monday;
        f.ShortDatePattern = "dd.MM.yyyy";
        f.ShortTimePattern = "HH:mm";
        f.LongTimePattern = "HH:mm:ss";
        culture.NumberFormat.NumberDecimalSeparator = ",";
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>Склонение по числу: Plural(1, "запись", "записи", "записей") → «запись», 3 → «записи», 11 → «записей».</summary>
    public static string Plural(long n, string one, string few, string many)
    {
        var abs = Math.Abs(n);
        var mod100 = abs % 100;
        var mod10 = abs % 10;
        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return mod10 switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string DayShort(DayOfWeek day) => DaysShort[(int)day];

    public static string DayFull(DayOfWeek day) => DaysFull[(int)day];

    public static string MonthNominative(int month) => Capitalize(MonthsNominative[month - 1]);

    public static string MonthShortNominative(int month) => MonthsShortNominative[month - 1];

    public static string MonthGenitive(int month) => MonthsGenitive[month - 1];

    /// <summary>«10.11».</summary>
    public static string DayMonthNumeric(DateOnly d) => $"{d.Day:00}.{d.Month:00}";

    /// <summary>«10.11.2026».</summary>
    public static string DateNumeric(DateOnly d) => $"{d.Day:00}.{d.Month:00}.{d.Year:0000}";

    /// <summary>«10 нояб.».</summary>
    public static string DayMonthShort(DateOnly d) => $"{d.Day} {MonthsShort[d.Month - 1]}";

    /// <summary>«10 ноября».</summary>
    public static string DayMonth(DateOnly d) => $"{d.Day} {MonthsGenitive[d.Month - 1]}";

    /// <summary>«10 ноября 2026».</summary>
    public static string DayMonthYear(DateOnly d) => $"{d.Day} {MonthsGenitive[d.Month - 1]} {d.Year}";

    /// <summary>«Пн, 9 ноября» — заголовок группы в журнале; год добавляется, если он не текущий.</summary>
    public static string GroupHeader(DateOnly d, DateOnly today) =>
        d.Year == today.Year ? $"{DayShort(d.DayOfWeek)}, {DayMonth(d)}" : $"{DayShort(d.DayOfWeek)}, {DayMonthYear(d)}";

    /// <summary>«09:05». Значения от 1440 — время следующих суток («02:00»), ровно 1440 — «24:00».</summary>
    public static string Clock(int minute)
    {
        if (minute == TimeRange.MinutesPerDay)
        {
            return "24:00";
        }

        var m = ((minute % TimeRange.MinutesPerDay) + TimeRange.MinutesPerDay) % TimeRange.MinutesPerDay;
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>«02:00 +1» для времени после полуночи следующих суток, иначе как <see cref="Clock"/>.</summary>
    public static string ClockWithDayOffset(int minute)
    {
        if (minute <= TimeRange.MinutesPerDay)
        {
            return Clock(minute);
        }

        var days = minute / TimeRange.MinutesPerDay;
        return $"{Clock(minute)} +{days}";
    }

    /// <summary>«09:00 – 13:00» или «22:00 – 02:00 +1».</summary>
    public static string Range(TimeRange range) => $"{Clock(range.StartMinute)} – {ClockWithDayOffset(range.EndMinute)}";

    /// <summary>Подпись периода (F3.1): «Неделя 9–15 ноября 2026», «Ноябрь 2026», «2026 год».</summary>
    public static string PeriodLabel(Period p) => p.Kind switch
    {
        PeriodKind.Day => $"{Capitalize(DayFull(p.Start.DayOfWeek))}, {DayMonthYear(p.Start)}",
        PeriodKind.Week => $"Неделя {DateSpan(p.Start, p.End)}",
        PeriodKind.Month => $"{MonthNominative(p.Start.Month)} {p.Start.Year}",
        PeriodKind.Year => $"{p.Start.Year} год",
        PeriodKind.AllTime => $"Всё время · {DateSpan(p.Start, p.End)}",
        PeriodKind.Custom => p.Start == p.End ? DayMonthYear(p.Start) : DateSpan(p.Start, p.End),
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    /// <summary>Компактная запись диапазона дат: «9–15 ноября 2026», «30 ноября – 6 декабря 2026», «29 декабря 2025 – 4 января 2026».</summary>
    public static string DateSpan(DateOnly start, DateOnly end)
    {
        if (start == end)
        {
            return DayMonthYear(start);
        }

        if (start.Year != end.Year)
        {
            return $"{DayMonthYear(start)} – {DayMonthYear(end)}";
        }

        if (start.Month != end.Month)
        {
            return $"{DayMonth(start)} – {DayMonthYear(end)}";
        }

        return $"{start.Day}–{end.Day} {MonthsGenitive[end.Month - 1]} {end.Year}";
    }

    /// <summary>Короткий диапазон дат: «6 нояб.», «3–6 нояб.», «30 нояб. – 2 дек.», «29 дек. 2025 – 4 янв. 2026».</summary>
    public static string DateSpanShort(DateOnly start, DateOnly end)
    {
        if (start == end)
        {
            return DayMonthShort(start);
        }

        if (start.Year != end.Year)
        {
            return $"{DayMonthShort(start)} {start.Year} – {DayMonthShort(end)} {end.Year}";
        }

        return start.Month != end.Month
            ? $"{DayMonthShort(start)} – {DayMonthShort(end)}"
            : $"{start.Day}–{end.Day} {MonthsShort[end.Month - 1]}";
    }

    /// <summary>«10 нояб., 09:12» — момент времени (локальный) для плашки и архива блокнота.</summary>
    public static string DayMonthTime(DateTime local) => $"{DayMonthShort(DateOnly.FromDateTime(local))}, {local.Hour:00}:{local.Minute:00}";
}
