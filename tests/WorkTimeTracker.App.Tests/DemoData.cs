using WorkTimeTracker.Core.Services;

namespace WorkTimeTracker.App.Tests;

/// <summary>
/// Демо-данные для скриншотов и тестов: ~6 недель реалистичных записей с комментариями и парой ночных смен.
/// Детерминированы (фиксированный seed), пользовательские данные не трогаются — всё пишется во временную БД.
/// </summary>
public static class DemoData
{
    private static readonly string[] Morning =
    [
        "планёрка и разбор почты", "ревью кода", "разработка: экспорт отчётов", "созвон с клиентом",
        "исправление багов", "1:1 с руководителем", "документация API", "разработка: фильтры",
    ];

    private static readonly string[] Afternoon =
    [
        "разработка: статистика", "тестирование релиза", "поддержка пользователей", "исследование: графики",
        "рефакторинг модуля данных", "ревью задач на спринт", "созвон с командой дизайна", "разработка: импорт",
    ];

    private static string T(int minutes) => $"{minutes / 60:00}{minutes % 60:00}";

    public static async Task SeedAsync(EntryService entries, DateOnly today)
    {
        var random = new Random(42);
        var lines = new List<string>();
        for (var offset = 41; offset >= 1; offset--)
        {
            var date = today.AddDays(-offset);
            var d = $"{date.Day:00}.{date.Month:00}.{date.Year}";
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (weekend)
            {
                if (random.Next(4) == 0)
                {
                    lines.Add($"{d} {T(660)} {T(750 + (random.Next(4) * 15))} разбор задач на неделю");
                }

                continue;
            }

            if (random.Next(14) == 0)
            {
                continue; // отгул
            }

            var start = 510 + (random.Next(6) * 10);      // 08:30–09:20
            var lunch = 780 + (random.Next(3) * 10);      // 13:00–13:20
            lines.Add($"{d} {T(start)} {T(lunch)} {Morning[random.Next(Morning.Length)]}");
            var afterStart = 840 + (random.Next(3) * 5);  // 14:00–14:10
            var end = 1050 + (random.Next(8) * 10);       // 17:30–18:40
            lines.Add($"{d} {T(afterStart)} {T(end)} {Afternoon[random.Next(Afternoon.Length)]}");
            if (random.Next(5) == 0)
            {
                lines.Add($"{d} {T(1170)} {T(1245)} вечерний созвон с командой из Владивостока");
            }
        }

        // Пара ночных смен: выкатка релизов.
        var release1 = today.AddDays(-9);
        var release2 = today.AddDays(-23);
        lines.Add($"{release1.Day:00}.{release1.Month:00}.{release1.Year} 2230 0130 релиз 2.4 — ночная выкатка");
        lines.Add($"{release2.Day:00}.{release2.Month:00}.{release2.Year} 2300 0200 миграция базы данных");

        // Сегодня: утро и текущая работа.
        lines.Add($"{today.Day:00}.{today.Month:00}.{today.Year} 0910 1300 планёрка и разработка: журнал");
        lines.Add($"{today.Day:00}.{today.Month:00}.{today.Year} 1400 1620 созвон с клиентом по отчётам");

        var result = await entries.AddFromTextAsync(string.Join('\n', lines), today);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Демо-данные не прошли валидацию: " + string.Join("; ", result.Errors));
        }
    }
}
