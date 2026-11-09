using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;

namespace WorkTimeTracker.Data;

/// <summary>Миграция: номер версии и SQL-скрипт.</summary>
public sealed record Migration(int Version, string Name, string Sql);

/// <summary>Результат применения миграций.</summary>
public sealed record MigrationResult(int FromVersion, int ToVersion)
{
    public int Applied => ToVersion - FromVersion;
}

/// <summary>БД создана более новой версией приложения — открывать её небезопасно (F4.7).</summary>
public sealed class DatabaseTooNewException(int databaseVersion, int supportedVersion)
    : Exception($"Версия схемы базы данных ({databaseVersion}) новее, чем поддерживает эта версия программы ({supportedVersion}). Обновите программу.")
{
    public int DatabaseVersion { get; } = databaseVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

/// <summary>
/// Применяет миграции схемы (F4.4). Скрипты — embedded resources <c>Migrations/NNNN_*.sql</c>,
/// применяются по возрастанию номера, каждая — в своей транзакции вместе с записью в <c>schema_version</c>.
/// Миграции только добавляются; существующие никогда не редактируются.
/// </summary>
public sealed partial class Migrator(SqliteConnectionFactory factory, TimeProvider time)
{
    private const string ResourcePrefix = "WorkTimeTracker.Data.Migrations.";

    /// <summary>Миграции, встроенные в сборку, по возрастанию версии.</summary>
    public static IReadOnlyList<Migration> Embedded { get; } = LoadEmbedded();

    public static int LatestVersion => Embedded[^1].Version;

    public Task<int> GetVersionAsync(CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                EnsureVersionTable(c);
                return c.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 0;
            },
            ct);

    /// <summary>Применяет недостающие миграции.</summary>
    /// <param name="migrations">Набор миграций (по умолчанию — встроенные); параметр нужен для тестов.</param>
    /// <param name="ct">Токен отмены.</param>
    public Task<MigrationResult> MigrateAsync(IReadOnlyList<Migration>? migrations = null, CancellationToken ct = default)
    {
        var list = (migrations ?? Embedded).OrderBy(m => m.Version).ToList();
        return factory.RunAsync(
            c =>
            {
                EnsureVersionTable(c);
                var current = c.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 0;
                var latest = list.Count == 0 ? 0 : list[^1].Version;
                if (current > latest)
                {
                    throw new DatabaseTooNewException(current, latest);
                }

                var from = current;
                foreach (var m in list.Where(m => m.Version > current))
                {
                    using var tx = c.BeginTransaction();
                    c.Execute(m.Sql, transaction: tx);
                    c.Execute(
                        "INSERT INTO schema_version (version, applied_at) VALUES (@v, @at)",
                        new { v = m.Version, at = time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) },
                        tx);
                    tx.Commit();
                    current = m.Version;
                }

                return new MigrationResult(from, current);
            },
            ct);
    }

    private static void EnsureVersionTable(Microsoft.Data.Sqlite.SqliteConnection c) =>
        c.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL, applied_at TEXT NOT NULL)");

    private static List<Migration> LoadEmbedded()
    {
        var assembly = typeof(Migrator).Assembly;
        var result = new List<Migration>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var file = name[ResourcePrefix.Length..];
            var match = MigrationNameRegex().Match(file);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Неверное имя миграции: {file}");
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            result.Add(new Migration(int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture), file, reader.ReadToEnd()));
        }

        result.Sort((a, b) => a.Version.CompareTo(b.Version));
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i].Version != i + 1)
            {
                throw new InvalidOperationException($"Миграции должны идти подряд с 1, найдена {result[i].Name}");
            }
        }

        return result;
    }

    [GeneratedRegex(@"^(\d{4})_[a-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationNameRegex();
}
