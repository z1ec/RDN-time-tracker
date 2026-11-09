using WorkTimeTracker.Core.Parsing;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F1.2: многострочная вставка.</summary>
public class QuickEntryParserBatchTests
{
    private readonly QuickEntryParser _parser = new(Create());

    [Fact]
    public void EachNonEmptyLine_IsSeparateEntry()
    {
        var result = _parser.ParseMany("0900 1300 утро\r\n\r\n1400 1800 вечер\n  \n09.11 1000 1100", Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Lines.Count);
        Assert.Equal([1, 3, 5], result.Lines.Select(l => l.LineNumber));
        Assert.Equal("вечер", result.Lines[1].Draft.Comment);
        Assert.Equal(new DateOnly(2026, 11, 9), result.Lines[2].Draft.Date);
    }

    [Fact]
    public void Errors_AreReportedPerLine()
    {
        var result = _parser.ParseMany("0900 1300\n2500 1800\n0900\n1400 1500", Today);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(
            ["Строка 2: Часы должны быть от 0 до 23 — «2500»", "Строка 3: Не указано время окончания"],
            result.ErrorMessages);
    }

    [Fact]
    public void SingleLine_ErrorHasNoLinePrefix()
    {
        var result = _parser.ParseMany("\n2500 1800\n", Today);
        Assert.Equal(["Часы должны быть от 0 до 23 — «2500»"], result.ErrorMessages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n  \r\n")]
    [InlineData(null)]
    public void EmptyText_IsEmpty(string? text)
    {
        var result = _parser.ParseMany(text, Today);
        Assert.True(result.IsEmpty);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LoneCarriageReturn_SplitsLines() =>
        Assert.Equal(2, _parser.ParseMany("0900 1000\r1100 1200", Today).Lines.Count);
}
