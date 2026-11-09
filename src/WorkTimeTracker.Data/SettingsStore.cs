using System.Globalization;
using Dapper;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data;

/// <summary>
/// Настройки в таблице <c>settings</c> (ключ → строка). Неизвестные или повреждённые значения
/// заменяются значениями по умолчанию, а не приводят к ошибке.
/// </summary>
public sealed class SettingsStore(SqliteConnectionFactory factory) : ISettingsStore
{
    internal const string DailyNormKey = "daily_norm_minutes";
    internal const string WorkDaysKey = "work_days";
    internal const string ThemeKey = "theme";
    internal const string WindowKey = "window";

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                var map = c.Query<(string Key, string Value)>("SELECT key, value FROM settings")
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                var defaults = new AppSettings();
                return new AppSettings
                {
                    DailyNormMinutes = map.TryGetValue(DailyNormKey, out var norm) && int.TryParse(norm, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 1440
                        ? n
                        : defaults.DailyNormMinutes,
                    WorkDays = map.TryGetValue(WorkDaysKey, out var days) ? ParseDays(days) ?? defaults.WorkDays : defaults.WorkDays,
                    Theme = map.TryGetValue(ThemeKey, out var theme) && Enum.TryParse<ThemePreference>(theme, out var t) && Enum.IsDefined(t)
                        ? t
                        : defaults.Theme,
                    Window = map.TryGetValue(WindowKey, out var w) ? ParseWindow(w) : null,
                };
            },
            ct);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                using var tx = c.BeginTransaction();
                void Set(string key, string? value)
                {
                    if (value is null)
                    {
                        c.Execute("DELETE FROM settings WHERE key = @key", new { key }, tx);
                    }
                    else
                    {
                        c.Execute(
                            "INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                            new { key, value },
                            tx);
                    }
                }

                Set(DailyNormKey, settings.DailyNormMinutes.ToString(CultureInfo.InvariantCulture));
                Set(WorkDaysKey, string.Join(",", settings.WorkDays.Select(d => (int)d).Order()));
                Set(ThemeKey, settings.Theme.ToString());
                Set(WindowKey, settings.Window is { } w
                    ? string.Join(",", new[] { w.X, w.Y, w.Width, w.Height, w.IsMaximized ? 1 : 0 }.Select(v => v.ToString(CultureInfo.InvariantCulture)))
                    : null);
                tx.Commit();
                return true;
            },
            ct);

    private static HashSet<DayOfWeek>? ParseDays(string s)
    {
        if (s.Length == 0)
        {
            return [];
        }

        var result = new HashSet<DayOfWeek>();
        foreach (var part in s.Split(','))
        {
            if (!int.TryParse(part, CultureInfo.InvariantCulture, out var v) || v is < 0 or > 6)
            {
                return null;
            }

            result.Add((DayOfWeek)v);
        }

        return result;
    }

    private static WindowPlacement? ParseWindow(string s)
    {
        var parts = s.Split(',');
        if (parts.Length != 5)
        {
            return null;
        }

        var v = new int[5];
        for (var i = 0; i < 5; i++)
        {
            if (!int.TryParse(parts[i], CultureInfo.InvariantCulture, out v[i]))
            {
                return null;
            }
        }

        return v[2] > 0 && v[3] > 0 ? new WindowPlacement(v[0], v[1], v[2], v[3], v[4] == 1) : null;
    }
}
