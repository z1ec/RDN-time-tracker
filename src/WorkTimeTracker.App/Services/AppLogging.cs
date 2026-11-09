using Serilog;

namespace WorkTimeTracker.App.Services;

/// <summary>Логи Serilog (F7.10): <c>logs\app-YYYYMMDD.log</c> в папке данных, ротация по дням, 7 файлов.</summary>
public static class AppLogging
{
    public static void Configure(string logsDirectory)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logsDirectory = Path.Combine(Path.GetTempPath(), "WorkTimeTracker", "logs");
            Directory.CreateDirectory(logsDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logsDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
