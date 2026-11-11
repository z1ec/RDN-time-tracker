using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorkTimeTracker.Data;

/// <summary>
/// Резервные копии (F4.5): копия через SQLite Backup API в <c>backups\worktime-YYYY-MM-DD.db</c>,
/// храним последние 14 копий. Автобэкап — раз в день при запуске; перед обновлением схемы — отдельная копия
/// в <c>backups\before-migration</c>, которая не ротируется.
/// </summary>
public sealed class BackupService(SqliteConnectionFactory factory, DataPaths paths, TimeProvider time)
{
    public const int KeepCount = 14;
    private const string Prefix = "worktime-";

    public string BackupPathFor(DateOnly date) =>
        Path.Combine(paths.BackupsDirectory, $"{Prefix}{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db");

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>Делает копию за сегодня (перезаписывает сегодняшнюю, если она есть) и удаляет старые.</summary>
    /// <returns>Путь к созданной копии.</returns>
    public Task<string> BackupNowAsync(CancellationToken ct = default) =>
        Task.Run(
            () =>
            {
                Directory.CreateDirectory(paths.BackupsDirectory);
                var target = BackupPathFor(Today);
                Copy(target, overwrite: true);
                Rotate();
                return target;
            },
            ct);

    /// <summary>Папка копий перед миграциями схемы: их не трогают ни ротация, ни ручная копия.</summary>
    public const string BeforeMigrationFolder = "before-migration";

    /// <summary>
    /// Копия перед обновлением схемы — делается всегда, даже если копия за сегодня уже есть:
    /// <c>backups\before-migration\worktime-v3-to-v4-YYYY-MM-DD-HHmmss.db</c>. Не ротируется и не перезаписывается.
    /// </summary>
    /// <returns>Путь к созданной копии.</returns>
    public Task<string> BeforeMigrationBackupAsync(int fromVersion, int toVersion, CancellationToken ct = default) =>
        Task.Run(
            () =>
            {
                var folder = Path.Combine(paths.BackupsDirectory, BeforeMigrationFolder);
                Directory.CreateDirectory(folder);
                var stamp = time.GetLocalNow().ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
                var target = Path.Combine(folder, $"{Prefix}v{fromVersion}-to-v{toVersion}-{stamp}.db");
                Copy(target, overwrite: false);
                return target;
            },
            ct);

    /// <summary>Автобэкап при запуске: если копии за сегодня ещё нет — создаёт её.</summary>
    /// <returns>Путь к новой копии или <c>null</c>, если копия за сегодня уже есть.</returns>
    public async Task<string?> AutoBackupAsync(CancellationToken ct = default)
    {
        if (File.Exists(BackupPathFor(Today)))
        {
            return null;
        }

        return await BackupNowAsync(ct).ConfigureAwait(false);
    }

    /// <summary>SQLite Backup API во временный файл, затем атомарное переименование.</summary>
    private void Copy(string target, bool overwrite)
    {
        var temp = target + ".tmp";
        File.Delete(temp);
        using (var source = factory.Open())
        using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
        {
            dest.Open();
            source.BackupDatabase(dest);
        }

        File.Move(temp, target, overwrite);
    }

    /// <summary>Список копий от новых к старым.</summary>
    public IReadOnlyList<string> ListBackups() =>
        Directory.Exists(paths.BackupsDirectory)
            ? [.. Directory.GetFiles(paths.BackupsDirectory, Prefix + "*.db").OrderByDescending(Path.GetFileName, StringComparer.Ordinal)]
            : [];

    /// <summary>Оставляет <see cref="KeepCount"/> самых новых копий.</summary>
    public int Rotate()
    {
        var removed = 0;
        foreach (var old in ListBackups().Skip(KeepCount))
        {
            File.Delete(old);
            removed++;
        }

        return removed;
    }
}
