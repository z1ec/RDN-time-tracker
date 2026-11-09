using WorkTimeTracker.Core.Parsing;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F1.1, F1.4: эталонные примеры из задания + пограничные случаи. «Сегодня» — 10.11.2026 14:37.</summary>
public class QuickEntryParserTests
{
    private readonly QuickEntryParser _parser = new(Create());

    public static TheoryData<string, string, int, int, string> Valid => new()
    {
        // ---- Таблица F1.4 из задания ----
        { "0900 1800", "10.11.2026", 540, 1080, "" },
        { "900 1800", "10.11.2026", 540, 1080, "" },
        { "9 18", "10.11.2026", 540, 1080, "" },
        { "09:00 18:30", "10.11.2026", 540, 1110, "" },
        { "9:05-13:40", "10.11.2026", 545, 820, "" },
        { "0900 - 1800", "10.11.2026", 540, 1080, "" },
        { "0900—1800", "10.11.2026", 540, 1080, "" },
        { "   0900    1800   ", "10.11.2026", 540, 1080, "" },
        { "0900 1800 созвон с клиентом", "10.11.2026", 540, 1080, "созвон с клиентом" },
        { "2200 0200", "10.11.2026", 1320, 1560, "" },
        { "0000 2400", "10.11.2026", 0, 1440, "" },
        { "15.03 0900 1800 отчёт", "15.03.2026", 540, 1080, "отчёт" },
        { "28.12 1000 1200", "28.12.2025", 600, 720, "" },
        { "15.03.2025 0900 1800", "15.03.2025", 540, 1080, "" },
        { "0900 сейчас", "10.11.2026", 540, 877, "" },
        // ---- Пограничные случаи ----
        { "0900–1800", "10.11.2026", 540, 1080, "" },                // короткое тире
        { "0900-1800", "10.11.2026", 540, 1080, "" },
        { "0900 -1800", "10.11.2026", 540, 1080, "" },
        { "0900- 1800", "10.11.2026", 540, 1080, "" },
        { "9:30 18", "10.11.2026", 570, 1080, "" },
        { "0 1", "10.11.2026", 0, 60, "" },
        { "2359 2400", "10.11.2026", 1439, 1440, "" },
        { "2300 0000", "10.11.2026", 1380, 1440, "" },               // конец в полночь = 24:00
        { "0001 0000", "10.11.2026", 1, 1440, "" },
        { "0100 0000", "10.11.2026", 60, 1440, "" },
        { "1200 1159", "10.11.2026", 720, 2159, "" },                // почти 24 ч через полночь
        { "1000 0959", "10.11.2026", 600, 2039, "" },
        { "0900 now", "10.11.2026", 540, 877, "" },
        { "сейчас 1800", "10.11.2026", 877, 1080, "" },
        { "0900 СЕЙЧАС", "10.11.2026", 540, 877, "" },
        { "10.11 0900 1000", "10.11.2026", 540, 600, "" },           // сегодняшняя дата без года
        { "1.1 0900 1000", "01.01.2026", 540, 600, "" },
        { "29.02.2024 0900 1000", "29.02.2024", 540, 600, "" },      // високосный год
        { "0900 1800 3 задачи", "10.11.2026", 540, 1080, "3 задачи" }, // число в комментарии — не интервал
        { "0900 1800   обед  в 13 ", "10.11.2026", 540, 1080, "обед  в 13" },
        { "0900\t1800\tзадача", "10.11.2026", 540, 1080, "задача" },
        { "0900 1800 «кавычки»; и точка-с-запятой", "10.11.2026", 540, 1080, "«кавычки»; и точка-с-запятой" },
        { "9:00—18:00 ревью", "10.11.2026", 540, 1080, "ревью" },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void Parses_ValidInput(string input, string date, int start, int end, string comment)
    {
        var result = _parser.Parse(input, Today);

        Assert.True(result.Status == ParseStatus.Success, result.Error?.DisplayText);
        var draft = result.Draft!;
        Assert.Equal(D(date), draft.Date);
        Assert.Equal(start, draft.Range.StartMinute);
        Assert.Equal(end, draft.Range.EndMinute);
        Assert.Equal(comment, draft.Comment);
    }

    public static TheoryData<string, string> Invalid => new()
    {
        // ---- Таблица F1.4 из задания ----
        { "1800 1800", "Нулевая длительность" },
        { "2500 1800", "Часы должны быть от 0 до 23" },
        { "0960 1800", "Минуты должны быть от 0 до 59" },
        { "2400 0100", "24:00 допустимо только как время окончания" },
        { "0900", "Не указано время окончания" },
        { "abc", "Не удалось распознать время начала" },
        { "12345 1800", "Неверный формат времени: 12345" },
        { "0900 1300 1400 1800", "Похоже на второй интервал" },
        { "31.02 0900 1000", "Несуществующая дата" },
        { "11.11.2026 0900 1000", "Дата в будущем" },
        // ---- Пограничные случаи ----
        { "0900 abc", "Не удалось распознать время окончания" },
        { "0900 -", "Не указано время окончания" },
        { "0900 1300-1400", "Похоже на второй интервал" },
        { "0900 1300 14-18", "Похоже на второй интервал" },
        { "0900 1300 14:00 обед", "Похоже на второй интервал" },
        { "09.30 18.30", "Несуществующая дата" },
        { "9.30-18.00", "Время с точкой не поддерживается" },
        { "0900 18.30", "Время с точкой не поддерживается" },
        { "15.03", "Не указано время начала" },
        { "29.02.2025 0900 1000", "Несуществующая дата" },
        { "00.01 0900 1000", "Несуществующая дата" },
        { "01.13 0900 1000", "Несуществующая дата" },
        { "0900abc 1800", "Неверный формат времени: 0900abc" },
        { "0900 1800abc", "Неверный формат времени: 1800abc" },
        { "9:5 18", "Неверный формат времени: 9:5" },
        { "2430 0100", "Часы должны быть от 0 до 23" },
        { "0900 2401", "Часы должны быть от 0 до 23" },
        { "+0900 1800", "Не удалось распознать время начала" },
        { "0900 +1800", "Не удалось распознать время окончания" },
        { "0000 0000", "Нулевая длительность" },
        { "сейчас сейчас", "Нулевая длительность" },
        { "01.01.2027 0900 1000", "Дата в будущем" },
        { "привет мир", "Не удалось распознать время начала" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Rejects_InvalidInput(string input, string expectedMessage)
    {
        var result = _parser.Parse(input, Today);

        Assert.Equal(ParseStatus.Error, result.Status);
        Assert.StartsWith(expectedMessage, result.Error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t ")]
    [InlineData(null)]
    public void EmptyInput_IsIgnored(string? input)
    {
        var result = _parser.Parse(input, Today);
        Assert.Equal(ParseStatus.Empty, result.Status);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NightShift_BelongsToStartDate()
    {
        var draft = _parser.Parse("2200 0200", Today).Draft!;
        Assert.Equal(Today, draft.Date);
        Assert.True(draft.Range.EndsNextDay);
        Assert.Equal(240, draft.Range.DurationMinutes);
    }

    [Fact]
    public void FullDay_Is24Hours()
    {
        var draft = _parser.Parse("0000 2400", Today).Draft!;
        Assert.Equal(1440, draft.Range.DurationMinutes);
        Assert.False(draft.Range.EndsNextDay);
    }

    [Fact]
    public void SelectedDate_IsUsedWhenNoDateInLine()
    {
        var selected = new DateOnly(2026, 11, 2);
        Assert.Equal(selected, _parser.Parse("0900 1000", selected).Draft!.Date);
    }

    [Fact]
    public void SelectedDate_InFuture_IsRejected() =>
        Assert.Equal("Дата в будущем", _parser.Parse("0900 1000", Today.AddDays(1)).Error!.Message);

    [Fact]
    public void DateWithoutYear_InJanuary_MeansPreviousDecember()
    {
        var parser = new QuickEntryParser(Create(2027, 1, 5));
        Assert.Equal(new DateOnly(2026, 12, 28), parser.Parse("28.12 1000 1200", new DateOnly(2027, 1, 5)).Draft!.Date);
    }

    [Fact]
    public void Now_UsesMinutePrecision()
    {
        var parser = new QuickEntryParser(Create(hour: 23, minute: 59));
        Assert.Equal(1439, parser.Parse("2300 сейчас", Today).Draft!.Range.EndMinute);
    }

    [Fact]
    public void CommentLongerThan500_IsRejected()
    {
        var result = _parser.Parse("0900 1000 " + new string('я', 501), Today);
        Assert.Equal("Комментарий длиннее 500 символов", result.Error!.Message);
        Assert.Equal(ParseStatus.Success, _parser.Parse("0900 1000 " + new string('я', 500), Today).Status);
    }

    [Fact]
    public void Error_PointsToProblemFragment()
    {
        var error = _parser.Parse("0900 2500", Today).Error!;
        Assert.Equal(5, error.Position);
        Assert.Equal(4, error.Length);
        Assert.Equal("2500", error.Fragment);
        Assert.Equal("Часы должны быть от 0 до 23 — «2500»", error.DisplayText);
    }

    [Fact]
    public void Error_FragmentAlreadyInMessage_IsNotDuplicated() =>
        Assert.Equal("Неверный формат времени: 12345", _parser.Parse("12345 1800", Today).Error!.DisplayText);

    [Fact]
    public void Error_MissingEnd_HasEmptyFragment()
    {
        var error = _parser.Parse("0900", Today).Error!;
        Assert.Equal(4, error.Position);
        Assert.Equal(0, error.Length);
        Assert.Equal("Не указано время окончания", error.DisplayText);
    }

    [Fact]
    public void FutureDateError_PointsToDate() =>
        Assert.Equal("11.11.2026", _parser.Parse("11.11.2026 0900 1000", Today).Error!.Fragment);

    [Fact]
    public void ZeroDurationError_PointsToInterval() =>
        Assert.Equal("1800 1800", _parser.Parse("1800 1800 текст", Today).Error!.Fragment);

    [Fact]
    public void DotTimeAsDate_GivesHint() =>
        Assert.Contains("Время с точкой не поддерживается", _parser.Parse("09.30 1800", Today).Error!.Message, StringComparison.Ordinal);

    [Fact]
    public void Today_ComesFromTimeProvider() => Assert.Equal(Today, _parser.Today);
}
