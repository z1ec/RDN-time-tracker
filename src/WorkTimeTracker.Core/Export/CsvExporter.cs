using System.Globalization;
using System.Text;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Export;

/// <summary>
/// Экспорт записей в CSV для русского Excel (F3.5): UTF-8 с BOM, разделитель «;», переводы строк CRLF,
/// десятичная запятая.
/// </summary>
public static class CsvExporter
{
    public const string Header = "Дата;Начало;Конец;Через полночь;Длительность (мин);Длительность (ч);Комментарий";

    private const string NewLine = "\r\n";

    /// <summary>Формирует текст CSV (без BOM) — записи сортируются по дате и времени начала.</summary>
    public static string ToCsv(IEnumerable<WorkEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append(NewLine);
        foreach (var e in entries.OrderBy(e => e.Date).ThenBy(e => e.StartMinute))
        {
            sb.Append(RuFormat.DateNumeric(e.Date)).Append(';')
              .Append(RuFormat.Clock(e.StartMinute)).Append(';')
              .Append(RuFormat.Clock(e.EndMinute)).Append(';')
              .Append(e.EndsNextDay ? "да" : "нет").Append(';')
              .Append(e.DurationMinutes.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append((e.DurationMinutes / 60.0).ToString("0.00", RuFormat.Culture)).Append(';')
              .Append(Escape(e.Comment))
              .Append(NewLine);
        }

        return sb.ToString();
    }

    /// <summary>Записывает CSV в поток в кодировке UTF-8 с BOM.</summary>
    public static async Task WriteAsync(IEnumerable<WorkEntry> entries, Stream stream, CancellationToken ct = default)
    {
        var bytes = ToBytes(entries);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    /// <summary>CSV в байтах: UTF-8 с BOM.</summary>
    public static byte[] ToBytes(IEnumerable<WorkEntry> entries)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(ToCsv(entries))];
    }

    /// <summary>Экранирует поле: если есть «;», кавычки или переводы строк — берёт в кавычки, кавычки удваивает.</summary>
    public static string Escape(string field)
    {
        if (field.IndexOfAny([';', '"', '\r', '\n']) < 0)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
