using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Formatting;

/// <summary>Тексты вкладки «Блокнот» (F8.6, F8.15).</summary>
public static class NotepadFormat
{
    public const string EntriesDeleted = "записи удалены";

    /// <summary>«7 ч 30 мин · 2 записи».</summary>
    public static string Totals(int totalMinutes, int count) =>
        $"{DurationFormatter.Format(totalMinutes)} · {count} {RuFormat.Plural(count, "запись", "записи", "записей")}";

    /// <summary>Плашка (F8.6): «В блокноте: 7 ч 30 мин · 2 записи · ведётся с 10 нояб., 09:12».</summary>
    public static string Summary(NotepadSheetView? current, TimeZoneInfo zone)
    {
        var totals = $"В блокноте: {Totals(current?.TotalMinutes ?? 0, current?.Entries.Count ?? 0)}";
        return current is null ? totals : $"{totals} · ведётся с {RuFormat.DayMonthTime(Local(current.Sheet.StartedAt, zone))}";
    }

    /// <summary>Название карточки архива: название листа, иначе «Лист 3–6 нояб.» по датам записей (без записей — по дате очистки).</summary>
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

    /// <summary>Диапазон дат записей листа: «3–6 нояб.»; если записей не осталось — «записи удалены» (F8.16).</summary>
    public static string DateRange(NotepadSheetView view) =>
        view.FirstDate is { } first ? RuFormat.DateSpanShort(first, view.LastDate!.Value) : EntriesDeleted;

    /// <summary>«очищен 6 нояб., 18:40».</summary>
    public static string Cleared(NotepadSheetView view, TimeZoneInfo zone) =>
        view.Sheet.ClearedAt is { } cleared ? $"очищен {RuFormat.DayMonthTime(Local(cleared, zone))}" : string.Empty;

    private static DateTime Local(DateTimeOffset t, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(t, zone).DateTime;
}
