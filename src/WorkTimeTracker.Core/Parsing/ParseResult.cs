using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Parsing;

/// <summary>Состояние разбора одной строки.</summary>
public enum ParseStatus
{
    /// <summary>Пустая строка или только пробелы — игнорируется без ошибки.</summary>
    Empty,
    Success,
    Error,
}

/// <summary>Ошибка разбора: текст для пользователя и проблемный фрагмент строки.</summary>
/// <param name="Message">Сообщение, например «Часы должны быть от 0 до 23».</param>
/// <param name="Position">Позиция фрагмента в исходной строке.</param>
/// <param name="Length">Длина фрагмента (0 — фрагмента нет, например не хватает данных в конце).</param>
/// <param name="Fragment">Сам фрагмент (может быть пустым).</param>
public sealed record ParseError(string Message, int Position, int Length, string Fragment)
{
    /// <summary>Сообщение с фрагментом: «Часы должны быть от 0 до 23 — «2500»».</summary>
    public string DisplayText =>
        Fragment.Length == 0 || Message.Contains(Fragment, StringComparison.Ordinal) ? Message : $"{Message} — «{Fragment}»";
}

/// <summary>Результат разбора одной строки быстрого ввода.</summary>
public sealed record ParseResult(ParseStatus Status, EntryDraft? Draft, ParseError? Error)
{
    public static ParseResult Empty { get; } = new(ParseStatus.Empty, null, null);

    public static ParseResult Ok(EntryDraft draft) => new(ParseStatus.Success, draft, null);

    public static ParseResult Fail(ParseError error) => new(ParseStatus.Error, null, error);
}

/// <summary>Распознанная строка многострочного ввода.</summary>
public sealed record ParsedLine(int LineNumber, EntryDraft Draft);

/// <summary>Ошибка в строке многострочного ввода (номер строки — с 1).</summary>
public sealed record LineError(int LineNumber, ParseError Error);

/// <summary>Результат разбора многострочного ввода (F1.2).</summary>
public sealed record BatchParseResult(IReadOnlyList<ParsedLine> Lines, IReadOnlyList<LineError> Errors, int NonEmptyLineCount)
{
    public bool IsEmpty => NonEmptyLineCount == 0;

    public bool IsSuccess => Errors.Count == 0 && Lines.Count > 0;

    /// <summary>
    /// Тексты ошибок. Если непустая строка одна — без префикса, иначе «Строка 3: …».
    /// </summary>
    public IReadOnlyList<string> ErrorMessages =>
        [.. Errors.Select(e => NonEmptyLineCount > 1 ? $"Строка {e.LineNumber}: {e.Error.DisplayText}" : e.Error.DisplayText)];
}
