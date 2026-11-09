using Microsoft.Extensions.Time.Testing;

namespace WorkTimeTracker.Data.Tests;

/// <summary>Временная БД на диске для теста: папка данных, фабрика соединений, часы, мигратор.</summary>
public sealed class TestDb : IDisposable
{
    public TestDb()
    {
        Paths = new DataPaths(Dir.Combine("data"), DataLocationKind.CommandLine);
        Paths.EnsureCreated();
        Factory = new SqliteConnectionFactory(Paths.DatabasePath);
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 37, 0, TimeSpan.Zero));
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Migrator = new Migrator(Factory, Time);
        Entries = new EntryRepository(Factory, Time);
        Settings = new SettingsStore(Factory);
        Backups = new BackupService(Factory, Paths, Time);
    }

    public TempDirectory Dir { get; } = new();

    public DataPaths Paths { get; }

    public SqliteConnectionFactory Factory { get; }

    public FakeTimeProvider Time { get; }

    public Migrator Migrator { get; }

    public EntryRepository Entries { get; }

    public SettingsStore Settings { get; }

    public BackupService Backups { get; }

    public DatabaseInitializer Initializer => new(Paths, Factory, Migrator, Backups, Entries, Time);

    public static async Task<TestDb> CreateMigratedAsync()
    {
        var db = new TestDb();
        await db.Migrator.MigrateAsync(ct: TestContext.Current.CancellationToken);
        return db;
    }

    public void Dispose() => Dir.Dispose();
}
