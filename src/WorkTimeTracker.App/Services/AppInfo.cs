using System.Reflection;

namespace WorkTimeTracker.App.Services;

/// <summary>Сведения о сборке приложения (F7.5).</summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/z1ec/RDN-time-tracker";

    /// <summary>Версия продукта без метаданных сборки (часть после «+» отбрасывается).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}
