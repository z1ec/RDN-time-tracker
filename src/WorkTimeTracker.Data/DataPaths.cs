namespace WorkTimeTracker.Data;

/// <summary>Как выбрана папка данных.</summary>
public enum DataLocationKind
{
    /// <summary>Стандартная папка пользователя: <c>%LOCALAPPDATA%\WorkTimeTracker</c>.</summary>
    Default,

    /// <summary>Портативный режим: рядом с exe лежит <c>portable.txt</c>, данные в <c>.\data</c> (F4.3).</summary>
    Portable,

    /// <summary>Папка задана аргументом <c>--data-dir</c> (F4.2).</summary>
    CommandLine,
}

/// <summary>Пути к файлам данных приложения (F4.2, F4.3).</summary>
public sealed record DataPaths(string DataDirectory, DataLocationKind Kind)
{
    public const string AppFolderName = "WorkTimeTracker";
    public const string DatabaseFileName = "worktime.db";
    public const string PortableMarkerFileName = "portable.txt";

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// Определяет папку данных. Приоритет: <c>--data-dir</c> → портативный режим → LocalApplicationData.
    /// </summary>
    /// <param name="commandLineDir">Значение <c>--data-dir</c> или <c>null</c>.</param>
    /// <param name="appBaseDirectory">Папка exe (<see cref="AppContext.BaseDirectory"/>).</param>
    /// <param name="localAppData">Папка <see cref="Environment.SpecialFolder.LocalApplicationData"/>.</param>
    public static DataPaths Resolve(string? commandLineDir, string appBaseDirectory, string localAppData)
    {
        if (!string.IsNullOrWhiteSpace(commandLineDir))
        {
            return new DataPaths(Path.GetFullPath(commandLineDir), DataLocationKind.CommandLine);
        }

        if (File.Exists(Path.Combine(appBaseDirectory, PortableMarkerFileName)))
        {
            return new DataPaths(Path.GetFullPath(Path.Combine(appBaseDirectory, "data")), DataLocationKind.Portable);
        }

        return new DataPaths(Path.Combine(localAppData, AppFolderName), DataLocationKind.Default);
    }

    /// <summary>Определяет папку данных для текущего процесса.</summary>
    public static DataPaths ResolveForCurrentProcess(string? commandLineDir) =>
        Resolve(
            commandLineDir,
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create));

    /// <summary>Создаёт папку данных и подпапки, если их нет.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
