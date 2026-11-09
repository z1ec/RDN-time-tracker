using WorkTimeTracker.App.Services;

namespace WorkTimeTracker.App.Tests;

/// <summary>F7.11: режим --self-test без UI (то же, что CI делает с настоящим exe).</summary>
public class SelfTestTests
{
    [Fact]
    public void SelfTest_Succeeds_AndWritesLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wtt-selftest-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var console = new StringWriter();
            var code = SelfTest.Run(CommandLineOptions.Parse(["--self-test", "--data-dir", dir]), TimeProvider.System, console);

            var log = File.ReadAllText(Path.Combine(dir, "self-test.log"));
            Assert.True(code == 0, log);
            Assert.Contains("РЕЗУЛЬТАТ: УСПЕХ", log, StringComparison.Ordinal);
            Assert.DoesNotContain("FAIL", log, StringComparison.Ordinal);
            Assert.Equal(log.Trim(), console.ToString().Trim());
            Assert.True(File.Exists(Path.Combine(dir, "self-test-db", "worktime.db")));
            Assert.False(File.Exists(Path.Combine(dir, "worktime.db"))); // пользовательская БД не создаётся

            // Повторный запуск в той же папке тоже успешен (папка self-test-db пересоздаётся).
            Assert.Equal(0, SelfTest.Run(CommandLineOptions.Parse(["--data-dir", dir]), TimeProvider.System, TextWriter.Null));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
