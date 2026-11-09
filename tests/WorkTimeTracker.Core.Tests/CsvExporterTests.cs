using System.Text;
using WorkTimeTracker.Core.Export;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F3.5: CSV для русского Excel.</summary>
public class CsvExporterTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    [Fact]
    public void Header_And_Rows_WithCrlf()
    {
        var csv = CsvExporter.ToCsv(
        [
            new WorkEntry(2, Day, 1320, 1560, "ночь"),
            new WorkEntry(1, Day, 540, 1080, "день"),
            new WorkEntry(3, Day.AddDays(-1), 0, 1440, ""),
        ]);

        Assert.Equal(
            "Дата;Начало;Конец;Через полночь;Длительность (мин);Длительность (ч);Комментарий\r\n" +
            "09.11.2026;00:00;24:00;нет;1440;24,00;\r\n" +
            "10.11.2026;09:00;18:00;нет;540;9,00;день\r\n" +
            "10.11.2026;22:00;02:00;да;240;4,00;ночь\r\n",
            csv);
    }

    [Theory]
    [InlineData("просто текст", "просто текст")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("сказал \"привет\"", "\"сказал \"\"привет\"\"\"")]
    [InlineData("строка1\nстрока2", "\"строка1\nстрока2\"")]
    [InlineData("строка1\r\nстрока2", "\"строка1\r\nстрока2\"")]
    [InlineData("", "")]
    public void Escape(string input, string expected) => Assert.Equal(expected, CsvExporter.Escape(input));

    [Fact]
    public void DurationHours_UsesDecimalComma() =>
        Assert.Contains(";95;1,58;", CsvExporter.ToCsv([new WorkEntry(1, Day, 600, 695, "")]), StringComparison.Ordinal);

    [Fact]
    public void Bytes_StartWithUtf8Bom()
    {
        var bytes = CsvExporter.ToBytes([new WorkEntry(1, Day, 540, 1080, "тест")]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("тест", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WritesSameBytes()
    {
        var entries = new[] { new WorkEntry(1, Day, 540, 1080, "a;b") };
        using var ms = new MemoryStream();
        await CsvExporter.WriteAsync(entries, ms, TestContext.Current.CancellationToken);
        Assert.Equal(CsvExporter.ToBytes(entries), ms.ToArray());
    }

    [Fact]
    public void Empty_HasOnlyHeader() => Assert.Equal(CsvExporter.Header + "\r\n", CsvExporter.ToCsv([]));
}
