using Microsoft.Data.Sqlite;

namespace WorkTimeTracker.Data;

/// <summary>
/// Открывает соединения с файлом БД (F4.6): WAL, внешние ключи, ожидание блокировки до 5 с.
/// Соединения короткоживущие и берутся из пула Microsoft.Data.Sqlite.
/// </summary>
public sealed class SqliteConnectionFactory(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    public string ConnectionString { get; } = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true,
        Pooling = true,
        DefaultTimeout = 5,
    }.ToString();

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            // Повреждённый файл: соединение нужно закрыть, иначе на Windows файл останется заблокированным.
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
            throw;
        }
    }

    /// <summary>Выполняет работу с БД в пуле потоков, чтобы не блокировать UI.</summary>
    public Task<T> RunAsync<T>(Func<SqliteConnection, T> work, CancellationToken ct = default) =>
        Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                using var connection = Open();
                return work(connection);
            },
            ct);
}
