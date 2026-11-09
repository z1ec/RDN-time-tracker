using System.Text.RegularExpressions;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.Core.Parsing;

/// <summary>
/// Разбор строки быстрого ввода (F1.1):
/// <c>[дата] &lt;начало&gt; &lt;разделитель&gt; &lt;конец&gt; [комментарий]</c>.
/// Полная грамматика и тексты ошибок — в <c>docs/INPUT_FORMAT.md</c>.
/// </summary>
public sealed partial class QuickEntryParser(TimeProvider time)
{
    public static class Messages
    {
        public const string StartNotRecognized = "Не удалось распознать время начала";
        public const string EndNotRecognized = "Не удалось распознать время окончания";
        public const string EndMissing = "Не указано время окончания";
        public const string StartMissing = "Не указано время начала";
        public const string InvalidFormat = "Неверный формат времени";
        public const string HoursRange = "Часы должны быть от 0 до 23";
        public const string MinutesRange = "Минуты должны быть от 0 до 59";
        public const string MidnightOnlyAsEnd = "24:00 допустимо только как время окончания";
        public const string DotTime = "Время с точкой не поддерживается — пишите 09:30 или 0930";
        public const string SecondInterval = "Похоже на второй интервал — вводите каждый интервал с новой строки";
        public const string NonexistentDate = "Несуществующая дата";
    }

    private enum Role
    {
        Start,
        End,
    }

    /// <summary>Сегодняшняя дата по часам <see cref="TimeProvider"/>.</summary>
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>Разбирает одну строку.</summary>
    /// <param name="line">Строка ввода.</param>
    /// <param name="selectedDate">Дата, выбранная в интерфейсе; используется, если дата не указана в строке.</param>
    public ParseResult Parse(string? line, DateOnly selectedDate)
    {
        var s = line ?? string.Empty;
        var i = SkipSpaces(s, 0);
        if (i == s.Length)
        {
            return ParseResult.Empty;
        }

        var today = Today;
        var date = selectedDate;
        var dateStart = -1;
        var dateLength = 0;

        // Необязательная дата — только первым токеном.
        var firstWordEnd = WordEnd(s, i);
        var firstWord = s[i..firstWordEnd];
        var dateMatch = DateRegex().Match(firstWord);
        if (dateMatch.Success)
        {
            var dateResult = ParseDate(dateMatch, today);
            if (dateResult is null)
            {
                var message = LooksLikeDotTime(dateMatch) ? $"{Messages.NonexistentDate}. {Messages.DotTime}" : Messages.NonexistentDate;
                return Fail(message, s, i, firstWord.Length);
            }

            date = dateResult.Value;
            dateStart = i;
            dateLength = firstWord.Length;
            i = SkipSpaces(s, firstWordEnd);
            if (i == s.Length)
            {
                return Fail(Messages.StartMissing, s, s.Length, 0);
            }
        }

        // Начало.
        var startPos = i;
        var start = ReadTime(s, ref i, Role.Start);
        if (start.Error is not null)
        {
            return ParseResult.Fail(start.Error);
        }

        if (start.Minute == TimeRange.MinutesPerDay)
        {
            return Fail(Messages.MidnightOnlyAsEnd, s, startPos, i - startPos);
        }

        // Разделитель: пробелы и/или одно тире (-, –, —).
        i = SkipSpaces(s, i);
        if (i < s.Length && IsDash(s[i]))
        {
            i = SkipSpaces(s, i + 1);
        }

        if (i == s.Length)
        {
            return Fail(Messages.EndMissing, s, s.Length, 0);
        }

        // Конец.
        var end = ReadTime(s, ref i, Role.End);
        if (end.Error is not null)
        {
            return ParseResult.Fail(end.Error);
        }

        var intervalEnd = i;

        // Комментарий — весь остаток строки.
        var comment = string.Empty;
        if (i < s.Length)
        {
            if (IsDash(s[i]))
            {
                return Fail(Messages.SecondInterval, s, i, s.Length - i);
            }

            var restStart = SkipSpaces(s, i);
            var rest = s[restStart..].TrimEnd();
            if (SecondIntervalRegex().IsMatch(rest))
            {
                return Fail(Messages.SecondInterval, s, restStart, rest.Length);
            }

            comment = rest;
        }

        var draft = EntryRules.CreateDraft(date, start.Minute, end.Minute, comment, today);
        if (!draft.IsSuccess)
        {
            return draft.Error switch
            {
                EntryRules.Messages.FutureDate when dateStart >= 0 => Fail(draft.Error, s, dateStart, dateLength),
                EntryRules.Messages.ZeroDuration => Fail(draft.Error, s, startPos, intervalEnd - startPos),
                _ => Fail(draft.Error!, s, 0, 0),
            };
        }

        return ParseResult.Ok(draft.Value!);
    }

    /// <summary>Разбирает многострочный ввод (F1.2): каждая непустая строка — отдельная запись.</summary>
    public BatchParseResult ParseMany(string? text, DateOnly selectedDate)
    {
        var lines = LineBreakRegex().Split(text ?? string.Empty);
        var parsed = new List<ParsedLine>();
        var errors = new List<LineError>();
        var nonEmpty = 0;
        for (var n = 0; n < lines.Length; n++)
        {
            var result = Parse(lines[n], selectedDate);
            switch (result.Status)
            {
                case ParseStatus.Success:
                    nonEmpty++;
                    parsed.Add(new ParsedLine(n + 1, result.Draft!));
                    break;
                case ParseStatus.Error:
                    nonEmpty++;
                    errors.Add(new LineError(n + 1, result.Error!));
                    break;
            }
        }

        return new BatchParseResult(parsed, errors, nonEmpty);
    }

    private readonly record struct TimeToken(int Minute, ParseError? Error);

    private TimeToken ReadTime(string s, ref int i, Role role)
    {
        var tokenStart = i;
        var notRecognized = role == Role.Start ? Messages.StartNotRecognized : Messages.EndNotRecognized;

        if (char.IsLetter(s[i]))
        {
            while (i < s.Length && char.IsLetter(s[i]))
            {
                i++;
            }

            var word = s[tokenStart..i];
            if (!IsBoundary(s, i))
            {
                var wordEnd = WordEnd(s, tokenStart);
                return Error(notRecognized, s, tokenStart, wordEnd - tokenStart);
            }

            if (word.Equals("сейчас", StringComparison.OrdinalIgnoreCase) || word.Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                var now = time.GetLocalNow();
                return new TimeToken((now.Hour * 60) + now.Minute, null);
            }

            return Error(notRecognized, s, tokenStart, word.Length);
        }

        if (!char.IsDigit(s[i]))
        {
            var wordEnd = WordEnd(s, tokenStart);
            return Error(notRecognized, s, tokenStart, Math.Max(1, wordEnd - tokenStart));
        }

        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == ':' || s[i] == '.'))
        {
            i++;
        }

        var token = s[tokenStart..i];
        if (!IsBoundary(s, i))
        {
            var wordEnd = WordEnd(s, tokenStart);
            var word = s[tokenStart..wordEnd];
            return Error($"{Messages.InvalidFormat}: {word}", s, tokenStart, word.Length);
        }

        if (token.Contains('.', StringComparison.Ordinal))
        {
            return Error(Messages.DotTime, s, tokenStart, token.Length);
        }

        var hours = -1;
        var minutes = -1;
        var colon = ColonTimeRegex().Match(token);
        if (colon.Success)
        {
            hours = int.Parse(colon.Groups[1].ValueSpan);
            minutes = int.Parse(colon.Groups[2].ValueSpan);
        }
        else if (DigitsRegex().IsMatch(token))
        {
            switch (token.Length)
            {
                case 1 or 2:
                    hours = int.Parse(token);
                    minutes = 0;
                    break;
                case 3:
                    hours = int.Parse(token.AsSpan(0, 1));
                    minutes = int.Parse(token.AsSpan(1));
                    break;
                case 4:
                    hours = int.Parse(token.AsSpan(0, 2));
                    minutes = int.Parse(token.AsSpan(2));
                    break;
            }
        }

        if (hours < 0)
        {
            return Error($"{Messages.InvalidFormat}: {token}", s, tokenStart, token.Length);
        }

        if (hours == 24 && minutes == 0)
        {
            return new TimeToken(TimeRange.MinutesPerDay, null);
        }

        if (hours > 23)
        {
            return Error(Messages.HoursRange, s, tokenStart, token.Length);
        }

        if (minutes > 59)
        {
            return Error(Messages.MinutesRange, s, tokenStart, token.Length);
        }

        return new TimeToken((hours * 60) + minutes, null);
    }

    private static DateOnly? ParseDate(Match m, DateOnly today)
    {
        var day = int.Parse(m.Groups[1].ValueSpan);
        var month = int.Parse(m.Groups[2].ValueSpan);
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return null;
        }

        int year;
        if (m.Groups[3].Success)
        {
            year = int.Parse(m.Groups[3].ValueSpan);
            if (year < 1)
            {
                return null;
            }
        }
        else
        {
            // Без года: если в текущем году дата ещё впереди — берём прошлый год (в январе «28.12» — прошлый декабрь).
            year = (month, day).CompareTo((today.Month, today.Day)) > 0 ? today.Year - 1 : today.Year;
        }

        return day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;
    }

    /// <summary>«09.30» без года — скорее время с точкой, чем дата.</summary>
    private static bool LooksLikeDotTime(Match m) =>
        !m.Groups[3].Success && int.Parse(m.Groups[1].ValueSpan) <= 24 && int.Parse(m.Groups[2].ValueSpan) > 12;

    private static TimeToken Error(string message, string s, int position, int length) =>
        new(-1, new ParseError(message, position, length, s.Substring(position, length)));

    private static ParseResult Fail(string message, string s, int position, int length) =>
        ParseResult.Fail(new ParseError(message, position, length, s.Substring(position, length)));

    private static bool IsDash(char c) => c is '-' or '–' or '—';

    private static bool IsBoundary(string s, int i) => i == s.Length || char.IsWhiteSpace(s[i]) || IsDash(s[i]);

    private static int SkipSpaces(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    private static int WordEnd(string s, int i)
    {
        while (i < s.Length && !char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    [GeneratedRegex(@"^(\d{1,2})\.(\d{1,2})(?:\.(\d{4}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(\d{1,2}):(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ColonTimeRegex();

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsRegex();

    /// <summary>
    /// Остаток начинается с токена, похожего на время: 3–4 цифры или H:mm, либо 1–2 цифры,
    /// за которыми идёт ещё одно время (например, «14 18» или «14-18»).
    /// </summary>
    [GeneratedRegex(
        @"^(?:(?:\d{3,4}|\d{1,2}:\d{2})(?=$|[\s\-–—])|\d{1,2}\s*[\s\-–—]\s*(?:\d{1,4}|\d{1,2}:\d{2})(?=$|\s))",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecondIntervalRegex();

    [GeneratedRegex(@"\r\n|\r|\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakRegex();
}
