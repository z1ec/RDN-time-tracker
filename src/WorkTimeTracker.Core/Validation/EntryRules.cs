using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Validation;

/// <summary>
/// Единые правила построения записи — общие для быстрого ввода (F1), формы (F2) и редактирования (F6.3).
/// Никакой другой код не должен дублировать эти проверки.
/// </summary>
public static class EntryRules
{
    public const int MaxCommentLength = 500;

    public static class Messages
    {
        public const string ZeroDuration = "Нулевая длительность";
        public const string FutureDate = "Дата в будущем";
        public const string CommentTooLong = "Комментарий длиннее 500 символов";
    }

    /// <summary>
    /// Проверяет дату, время и комментарий и собирает <see cref="EntryDraft"/>.
    /// </summary>
    /// <param name="date">Дата записи (дата начала).</param>
    /// <param name="startMinute">Начало, 0…1439.</param>
    /// <param name="endClockMinute">«Настенное» время окончания, 0…1440; меньше начала — ночная смена.</param>
    /// <param name="comment">Комментарий; пробелы по краям обрезаются.</param>
    /// <param name="today">Сегодняшняя дата (из <see cref="TimeProvider"/>).</param>
    public static Result<EntryDraft> CreateDraft(DateOnly date, int startMinute, int endClockMinute, string? comment, DateOnly today)
    {
        if (date > today)
        {
            return Result<EntryDraft>.Fail(Messages.FutureDate);
        }

        var range = TimeRange.FromClock(startMinute, endClockMinute);
        if (range is null)
        {
            return Result<EntryDraft>.Fail(Messages.ZeroDuration);
        }

        var text = (comment ?? string.Empty).Trim();
        if (text.Length > MaxCommentLength)
        {
            return Result<EntryDraft>.Fail(Messages.CommentTooLong);
        }

        return Result<EntryDraft>.Ok(new EntryDraft(date, range.Value, text));
    }

    /// <summary>Проверяет уже собранный черновик (интервал валиден по построению <see cref="TimeRange"/>).</summary>
    /// <returns>Текст ошибки или <c>null</c>.</returns>
    public static string? Validate(EntryDraft draft, DateOnly today) =>
        draft.Date > today ? Messages.FutureDate
        : draft.Comment.Length > MaxCommentLength ? Messages.CommentTooLong
        : null;
}

/// <summary>Результат операции: значение или текст ошибки для пользователя.</summary>
public readonly record struct Result<T>
{
    private Result(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public string? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(string error) => new(default, error);
}
