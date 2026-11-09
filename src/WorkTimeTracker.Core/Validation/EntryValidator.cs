using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Validation;

/// <summary>
/// Запрет пересечений записей (F7.1, ADR-0007). Сравнение — в абсолютных минутах,
/// поэтому ночная смена 10.11 22:00–02:00 пересекается с записью 11.11 01:00–03:00.
/// Касание (конец одной == начало другой) допустимо.
/// </summary>
public static class EntryValidator
{
    /// <summary>
    /// Максимальный сдвиг в днях, на котором записи могут пересечься: запись длится не больше 24 ч,
    /// поэтому кандидатов достаточно искать в диапазоне [дата − 1; дата + 1].
    /// </summary>
    public const int NeighbourDays = 1;

    public static bool Overlaps(long aStart, long aEnd, long bStart, long bEnd) => aStart < bEnd && bStart < aEnd;

    /// <summary>Находит первую существующую запись, пересекающуюся с черновиком.</summary>
    /// <param name="draft">Новая или изменённая запись.</param>
    /// <param name="existing">Существующие записи (удалённые сюда передавать не нужно).</param>
    /// <param name="excludeId">Id редактируемой записи — она не сравнивается сама с собой (F6.3).</param>
    public static WorkEntry? FindOverlap(EntryDraft draft, IEnumerable<WorkEntry> existing, long? excludeId = null) =>
        existing
            .Where(e => e.Id != excludeId)
            .OrderBy(e => e.AbsoluteStart)
            .FirstOrDefault(e => Overlaps(draft.AbsoluteStart, draft.AbsoluteEnd, e.AbsoluteStart, e.AbsoluteEnd));

    /// <summary>Текст ошибки: «Пересекается с записью 10.11 13:00–15:00 «обед»».</summary>
    public static string OverlapMessage(WorkEntry other)
    {
        var text = $"Пересекается с записью {RuFormat.DayMonthNumeric(other.Date)} {RuFormat.Clock(other.StartMinute)}–{RuFormat.Clock(other.EndMinute)}";
        return other.Comment.Length == 0 ? text : $"{text} «{Shorten(other.Comment, 40)}»";
    }

    /// <summary>Проверяет черновик на пересечения; возвращает текст ошибки или <c>null</c>.</summary>
    public static string? Validate(EntryDraft draft, IEnumerable<WorkEntry> existing, long? excludeId = null)
    {
        var other = FindOverlap(draft, existing, excludeId);
        return other is null ? null : OverlapMessage(other);
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
