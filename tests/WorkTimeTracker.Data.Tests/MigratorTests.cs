using Dapper;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F4.4: миграции схемы.</summary>
public class MigratorTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Embedded_AreSequential()
    {
        Assert.True(Migrator.Embedded.Count >= 2);
        Assert.Equal(Enumerable.Range(1, Migrator.Embedded.Count), Migrator.Embedded.Select(m => m.Version));
        Assert.Equal(Migrator.Embedded.Count, Migrator.LatestVersion);
        Assert.Equal("0001_initial.sql", Migrator.Embedded[0].Name);
    }

    [Fact]
    public async Task EmptyDatabase_MigratesToLatest()
    {
        using var db = new TestDb();
        Assert.Equal(0, await db.Migrator.GetVersionAsync(_ct));

        var result = await db.Migrator.MigrateAsync(ct: _ct);

        Assert.Equal(0, result.FromVersion);
        Assert.Equal(Migrator.LatestVersion, result.ToVersion);
        Assert.Equal(Migrator.LatestVersion, result.Applied);
        Assert.Equal(Migrator.LatestVersion, await db.Migrator.GetVersionAsync(_ct));
        using var c = db.Factory.Open();
        var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToList();
        Assert.Contains("entries", tables);
        Assert.Contains("settings", tables);
        Assert.Contains("schema_version", tables);
        Assert.Equal("wal", c.ExecuteScalar<string>("PRAGMA journal_mode"));
        Assert.Equal(1, c.ExecuteScalar<int>("PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task VersionN_MigratesToNPlusOne()
    {
        using var db = new TestDb();
        var first = Migrator.Embedded.Take(1).ToList();
        await db.Migrator.MigrateAsync(first, _ct);
        Assert.Equal(1, await db.Migrator.GetVersionAsync(_ct));

        var result = await db.Migrator.MigrateAsync(Migrator.Embedded.Take(2).ToList(), _ct);

        Assert.Equal(1, result.FromVersion);
        Assert.Equal(2, result.ToVersion);
        using var c = db.Factory.Open();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name = 'ix_entries_deleted_at'"));
    }

    [Fact]
    public async Task Repeated_DoesNothing()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var result = await db.Migrator.MigrateAsync(ct: _ct);
        Assert.Equal(0, result.Applied);
    }

    [Fact]
    public async Task FailedMigration_IsRolledBack()
    {
        using var db = new TestDb();
        var broken = new List<Migration>(Migrator.Embedded) { new(Migrator.LatestVersion + 1, "9999_broken.sql", "CREATE TABLE ok_part (x INT); SELECT * FROM missing_table;") };

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Migrator.MigrateAsync(broken, _ct));

        Assert.Equal(Migrator.LatestVersion, await db.Migrator.GetVersionAsync(_ct));
        using var c = db.Factory.Open();
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name = 'ok_part'"));
    }

    [Fact]
    public async Task NewerDatabase_IsRejected()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var ex = await Assert.ThrowsAsync<DatabaseTooNewException>(() => db.Migrator.MigrateAsync(Migrator.Embedded.Take(1).ToList(), _ct));
        Assert.Equal(Migrator.LatestVersion, ex.DatabaseVersion);
        Assert.Equal(1, ex.SupportedVersion);
    }
}
