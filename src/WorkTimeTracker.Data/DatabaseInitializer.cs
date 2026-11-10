using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data;

/// <summary>Итог подготовки БД при запуске.</summary>
public sealed record DatabaseStartupResult(MigrationResult Migration, string? AutoBackupPath, int PurgedEntries, NotepadPurgeResult PurgedNotepad);

/// <summary>
/// Не удалось открыть или подготовить БД (F4.7). Данные при этом не трогаются:
/// приложение показывает путь к файлу и предлагает открыть папку.
/// </summary>
public sealed class DatabaseOpenException(string databasePath, Exception inner)
    : Exception($"Не удалось открыть базу данных «{databasePath}»: {inner.Message}", inner)
{
    public string DatabasePath { get; } = databasePath;
}

/// <summary>
/// Подготовка БД при запуске: папки → автобэкап существующей БД (F4.5) → миграции (F4.4) →
/// физическое удаление мягко удалённых записей старше 30 дней (F5.3) и мусора блокнота по тому же правилу (F8.18).
/// БД никогда не удаляется и не пересоздаётся автоматически (F4.7).
/// </summary>
public sealed class DatabaseInitializer(DataPaths paths, SqliteConnectionFactory factory, Migrator migrator, BackupService backups, EntryRepository entries, NotepadRepository notepad, TimeProvider time)
{
    public static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(30);

    public async Task<DatabaseStartupResult> InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            paths.EnsureCreated();
            var existed = File.Exists(paths.DatabasePath);

            // Проверяем, что файл — действительно БД SQLite, до любых изменений.
            await factory.RunAsync(c => CheckReadable(c), ct).ConfigureAwait(false);

            // Копия делается до миграций: если миграция что-то испортит, останется исходная БД.
            var backup = existed ? await backups.AutoBackupAsync(ct).ConfigureAwait(false) : null;
            var migration = await migrator.MigrateAsync(ct: ct).ConfigureAwait(false);
            var purged = await entries.PurgeDeletedAsync(time.GetUtcNow() - PurgeAfter, ct).ConfigureAwait(false);
            var purgedNotepad = await notepad.PurgeAsync(time.GetUtcNow() - PurgeAfter, ct).ConfigureAwait(false);
            return new DatabaseStartupResult(migration, backup, purged, purgedNotepad);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or DatabaseTooNewException)
        {
            throw new DatabaseOpenException(paths.DatabasePath, ex);
        }
    }

    private static bool CheckReadable(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master";
        cmd.ExecuteScalar();
        return true;
    }
}
