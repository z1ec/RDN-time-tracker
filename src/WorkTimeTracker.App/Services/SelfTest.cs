using Microsoft.Data.Sqlite;

namespace WorkTimeTracker.App.Services;

/// <summary>
/// Режим <c>--self-test</c> (F7.11): проверка без UI. Минимальная версия этапа 1 —
/// проверяет, что нативный SQLite упакован в single-file exe и работает.
/// </summary>
internal static class SelfTest
{
    public static int Run(CommandLineOptions options, TimeProvider time, TextWriter console)
    {
        var dataDir = options.DataDir ?? Path.Combine(Path.GetTempPath(), "WorkTimeTracker-selftest");
        Directory.CreateDirectory(dataDir);
        var logPath = Path.Combine(dataDir, "self-test.log");
        using var log = new StreamWriter(logPath, append: false);

        void Report(string line)
        {
            log.WriteLine(line);
            console.WriteLine(line);
        }

        try
        {
            Report($"WorkTime Tracker {AppInfo.Version} — self-test, {time.GetUtcNow():O}");
            var dbPath = Path.Combine(dataDir, "worktime.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "select sqlite_version();";
            Report($"OK  SQLite {command.ExecuteScalar()}");
            Report("РЕЗУЛЬТАТ: УСПЕХ");
            return 0;
        }
        catch (Exception ex)
        {
            Report($"FAIL {ex}");
            Report("РЕЗУЛЬТАТ: ОШИБКА");
            return 1;
        }
    }
}
