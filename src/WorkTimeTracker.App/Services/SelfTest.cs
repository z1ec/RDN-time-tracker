using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.Core.Export;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Statistics;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App.Services;

/// <summary>
/// Режим <c>--self-test</c> (F7.11): проверка без UI на тех же сервисах, что и у приложения.
/// БД создаётся в подпапке <c>self-test-db</c> папки данных (пользовательская <c>worktime.db</c> не трогается),
/// отчёт пишется в <c>self-test.log</c> в папке данных. Код выхода: 0 — успех, 1 — ошибка.
/// CI запускает так настоящий Windows-exe, проверяя, что нативный SQLite упакован в single-file.
/// </summary>
internal static class SelfTest
{
    public const string LogFileName = "self-test.log";
    public const string DatabaseFolderName = "self-test-db";

    public static int Run(CommandLineOptions options, TimeProvider time, TextWriter console)
    {
        var dataDir = options.DataDir is { Length: > 0 } dir
            ? Path.GetFullPath(dir)
            : Path.Combine(Path.GetTempPath(), "WorkTimeTracker-selftest");
        Directory.CreateDirectory(dataDir);
        using var log = new StreamWriter(Path.Combine(dataDir, LogFileName), append: false);
        var reporter = new Reporter(log, console);
        reporter.Line($"WorkTime Tracker {AppInfo.Version} — self-test");
        reporter.Line($"Время: {time.GetLocalNow():yyyy-MM-dd HH:mm:ss zzz}; ОС: {Environment.OSVersion}; .NET {Environment.Version}; 64-бит: {Environment.Is64BitProcess}");
        reporter.Line($"Папка данных: {dataDir}");

        try
        {
            RunChecks(dataDir, time, reporter).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            reporter.Fail("Непредвиденная ошибка", ex.ToString());
        }

        var ok = reporter.Failed == 0;
        reporter.Line(ok
            ? $"РЕЗУЛЬТАТ: УСПЕХ ({reporter.Passed} проверок)"
            : $"РЕЗУЛЬТАТ: ОШИБКА (успешно {reporter.Passed}, ошибок {reporter.Failed})");
        return ok ? 0 : 1;
    }

    private static async Task RunChecks(string dataDir, TimeProvider time, Reporter r)
    {
        var dbDir = Path.Combine(dataDir, DatabaseFolderName);
        if (Directory.Exists(dbDir))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dbDir, recursive: true); // это собственная папка self-test, не пользовательские данные
        }

        var paths = new DataPaths(dbDir, DataLocationKind.CommandLine);
        await using var services = new ServiceCollection()
            .AddCoreServices(paths, time)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var entries = services.GetRequiredService<EntryService>();
        var repo = services.GetRequiredService<EntryRepository>();
        var today = entries.Today;

        var startup = await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        r.Check("БД создана, миграции применены", startup.Migration.ToVersion == Migrator.LatestVersion,
            $"схема v{startup.Migration.ToVersion}, файл {paths.DatabasePath}");

        var version = await services.GetRequiredService<SqliteConnectionFactory>().RunAsync(c =>
        {
            using var command = c.CreateCommand();
            command.CommandText = "select sqlite_version()";
            return command.ExecuteScalar() as string;
        });
        r.Check("Нативный SQLite загружен", version is string, $"SQLite {version}");

        var added = await entries.AddFromTextAsync("0900 1800 self-test", today);
        r.Check("Добавление из быстрого ввода", added.IsSuccess && added.Entries.Count == 1, string.Join("; ", added.Errors));
        var id = added.Entries[0].Id;

        var read = await repo.GetAsync(id);
        r.Check("Чтение записи", read is { StartMinute: 540, EndMinute: 1080, Comment: "self-test" }, read?.ToString());

        var draft = entries.BuildDraft(today, TimeSpan.FromHours(10), new TimeSpan(18, 30, 0), "Проверка ИЗМЕНЕНО");
        var updated = await entries.UpdateAsync(id, draft.Value!);
        var reread = await repo.GetAsync(id);
        r.Check("Редактирование записи", updated.IsSuccess && reread is { StartMinute: 600, EndMinute: 1110 }, reread?.ToString());

        var overlap = await entries.AddFromTextAsync("1100 1200", today);
        r.Check("Пересечение отклонено", !overlap.IsSuccess, overlap.Errors.FirstOrDefault());

        var night = await entries.AddFromTextAsync("2200 0200 ночная смена", today.AddDays(-1));
        r.Check("Ночная смена", night.IsSuccess && night.Entries[0].EndsNextDay, string.Join("; ", night.Errors));

        await entries.DeleteAsync([id]);
        var afterDelete = await repo.GetRangeAsync(today, today);
        await entries.UndoDeleteAsync([id]);
        var afterRestore = await repo.GetRangeAsync(today, today);
        r.Check("Мягкое удаление и восстановление", afterDelete.Count == 0 && afterRestore.Count == 1);

        var week = PeriodNavigator.ForDate(PeriodKind.Week, today);
        var stats = StatisticsCalculator.Calculate(week, await repo.GetRangeAsync(week.Start.AddDays(-7), week.End), today, new AppSettings());
        var expectedWeek = 510 + (PeriodNavigator.StartOfWeek(today.AddDays(-1)) == week.Start ? 240 : 0);
        r.Check("Статистика недели", stats.TotalMinutes == expectedWeek, $"{DurationFormatter.Format(stats.TotalMinutes)}, {RuFormat.PeriodLabel(week)}");

        var found = await repo.SearchAsync("изменено");
        r.Check("Поиск по кириллице без учёта регистра", found.Count == 1);

        var csv = CsvExporter.ToBytes(await repo.GetAllAsync());
        r.Check("Экспорт CSV (UTF-8 с BOM)", csv is [0xEF, 0xBB, 0xBF, ..] && System.Text.Encoding.UTF8.GetString(csv).Contains("ИЗМЕНЕНО", StringComparison.Ordinal));

        var settings = services.GetRequiredService<SettingsService>();
        await settings.LoadAsync();
        await settings.UpdateAsync(s => s with { DailyNormMinutes = 450, Theme = ThemePreference.Dark });
        var loaded = await services.GetRequiredService<SettingsStore>().LoadAsync();
        r.Check("Настройки сохраняются", loaded is { DailyNormMinutes: 450, Theme: ThemePreference.Dark });

        var backup = await services.GetRequiredService<BackupService>().BackupNowAsync();
        var backupCount = (await new EntryRepository(new SqliteConnectionFactory(backup), time).GetAllAsync()).Count;
        r.Check("Резервная копия", File.Exists(backup) && backupCount == 2, backup);

        r.Check("Русские форматы", RuFormat.Culture.Name == "ru-RU" && RuFormat.DayMonthYear(new DateOnly(2026, 11, 10)) == "10 ноября 2026");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private sealed class Reporter(TextWriter log, TextWriter console)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public int Passed { get; private set; }

        public int Failed { get; private set; }

        public void Line(string text)
        {
            log.WriteLine(text);
            log.Flush();
            try
            {
                console.WriteLine(text);
            }
            catch (IOException)
            {
                // Консоль недоступна (WinExe без родительской консоли) — достаточно файла.
            }
        }

        public void Check(string name, bool ok, string? details = null)
        {
            if (ok)
            {
                Passed++;
                Line($"OK    [{_clock.ElapsedMilliseconds,5} мс] {name}{(string.IsNullOrEmpty(details) ? "" : " — " + details)}");
            }
            else
            {
                Fail(name, details);
            }
        }

        public void Fail(string name, string? details)
        {
            Failed++;
            Line($"FAIL  [{_clock.ElapsedMilliseconds,5} мс] {name}{(string.IsNullOrEmpty(details) ? "" : " — " + details)}");
        }
    }
}
