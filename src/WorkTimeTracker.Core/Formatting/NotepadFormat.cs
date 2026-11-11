using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.Core.Formatting;

/// <summary>Тексты вкладки «Блокнот» (F8.6, F8.15, F9.3, F9.4).</summary>
public static class NotepadFormat
{
    public const string EntriesDeleted = "записи удалены";

    public const string NotInStatisticsLabel = "не в статистике";

    /// <summary>«7 ч 30 мин · 2 записи».</summary>
    public static string Totals(int totalMinutes, int count) =>
        $"{DurationFormatter.Format(totalMinutes)} · {Count(count)}";

    /// <summary>
    /// Суммы листа (F9.4): «7 ч 00 мин · 2 записи», а если сумма связанных записей в базе отличается —
    /// «7 ч 00 мин · в статистике: 4 ч 00 мин · 2 записи».
    /// </summary>
    public static string Totals(NotepadSheetView view) =>
        view.HasStatisticsDifference
            ? $"{DurationFormatter.Format(view.TotalMinutes)} · в статистике: {DurationFormatter.Format(view.StatisticsMinutes)} · {Count(view.Lines.Count)}"
            : Totals(view.TotalMinutes, view.Lines.Count);

    /// <summary>
    /// Плашка (F8.6, F9.4): «В блокноте: 7 ч 30 мин · 2 записи · ведётся с 10 нояб., 09:12»;
    /// если суммы расходятся — «В блокноте: 7 ч 00 мин · в статистике: 4 ч 00 мин · 2 записи · ведётся с …».
    /// </summary>
    public static string Summary(NotepadSheetView? current, TimeZoneInfo zone)
    {
        if (current is null)
        {
            return $"В блокноте: {Totals(0, 0)}";
        }

        return $"В блокноте: {Totals(current)} · ведётся с {RuFormat.DayMonthTime(Local(current.Sheet.StartedAt, zone))}";
    }

    /// <summary>Пояснение ко второй цифре на плашке или пустая строка, если суммы совпадают.</summary>
    public static string SummaryHint(NotepadSheetView? current) =>
        current is { HasStatisticsDifference: true }
            ? "Часть строк не записана в статистику: их время пересекается с записями журнала. Статистика считается только по журналу"
            : string.Empty;

    /// <summary>Метка статуса строки (F9.3): пусто, «не в статистике» или «в статистике: 09:00–19:00».</summary>
    public static string StatusLabel(NotepadLine line) => line.Status switch
    {
        NotepadLineStatus.NotInStatistics => NotInStatisticsLabel,
        NotepadLineStatus.DifferentInStatistics => $"в статистике: {EntryTime(line.Linked!, line.Date)}",
        _ => string.Empty,
    };

    /// <summary>
    /// Подсказка статуса (F9.3). Конфликт — на момент показа (<see cref="NotepadLine.Conflict"/>); если его уже нет,
    /// объясняется, почему строка не записана.
    /// </summary>
    public static string StatusHint(NotepadLine line) => line.Status switch
    {
        NotepadLineStatus.NotInStatistics => line.Conflict is { } c
            ? $"Пересекается с записью {EntryValidator.Describe(c)} — в статистику не записано"
            : "В статистику не записано: при добавлении было пересечение",
        NotepadLineStatus.DifferentInStatistics => line.Conflict is { } c
            ? $"Новое время пересекается с записью {EntryValidator.Describe(c)} — в статистике осталось прежнее"
            : "Новое время не записано в статистику: при правке было пересечение. В статистике осталось прежнее",
        _ => "Учтено в журнале и статистике",
    };

    /// <summary>Итог правки строки для строки состояния (F9.5).</summary>
    public static string Edited(NotepadLine line) => line.Status switch
    {
        NotepadLineStatus.InStatistics => "Строка изменена · в статистике",
        _ => $"Строка изменена только в блокноте · {StatusHint(line)}",
    };

    /// <summary>«Добавлено: 2 записи» или «Добавлено: 2 записи · не в статистике: 1» (F9.2).</summary>
    public static string Added(int count, int notInStatistics) =>
        notInStatistics == 0
            ? $"Добавлено: {Count(count)}"
            : $"Добавлено: {Count(count)} · не в статистике: {notInStatistics} (пересечение с журналом)";

    /// <summary>Название карточки архива: название листа, иначе «Лист 3–6 нояб.» по датам строк (без строк — по дате очистки).</summary>
    public static string SheetTitle(NotepadSheetView view, TimeZoneInfo zone)
    {
        if (view.Sheet.Title.Length > 0)
        {
            return view.Sheet.Title;
        }

        return view.FirstDate is { } first
            ? $"Лист {RuFormat.DateSpanShort(first, view.LastDate!.Value)}"
            : $"Лист от {RuFormat.DayMonthShort(DateOnly.FromDateTime(Local(view.Sheet.ClearedAt ?? view.Sheet.StartedAt, zone)))}";
    }

    /// <summary>Диапазон дат строк листа: «3–6 нояб.»; если строк не осталось — «записи удалены» (F8.16).</summary>
    public static string DateRange(NotepadSheetView view) =>
        view.FirstDate is { } first ? RuFormat.DateSpanShort(first, view.LastDate!.Value) : EntriesDeleted;

    /// <summary>«очищен 6 нояб., 18:40».</summary>
    public static string Cleared(NotepadSheetView view, TimeZoneInfo zone) =>
        view.Sheet.ClearedAt is { } cleared ? $"очищен {RuFormat.DayMonthTime(Local(cleared, zone))}" : string.Empty;

    private static string Count(int count) => $"{count} {RuFormat.Plural(count, "запись", "записи", "записей")}";

    /// <summary>«09:00–19:00»; если дата записи отличается от даты строки — «09.11 09:00–19:00».</summary>
    private static string EntryTime(WorkEntry entry, DateOnly lineDate)
    {
        var time = $"{RuFormat.Clock(entry.StartMinute)}–{RuFormat.Clock(entry.EndMinute)}";
        return entry.Date == lineDate ? time : $"{RuFormat.DayMonthNumeric(entry.Date)} {time}";
    }

    private static DateTime Local(DateTimeOffset t, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(t, zone).DateTime;
}
