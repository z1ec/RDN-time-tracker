using Dapper;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F4.4, F4.5, F4.7, F5.3: подготовка БД при запуске.</summary>
public class DatabaseInitializerTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstRun_CreatesDatabase_WithoutBackup()
    {
        using var db = new TestDb();
        var result = await db.Initializer.InitializeAsync(_ct);
        Assert.Equal(Migrator.LatestVersion, result.Migration.ToVersion);
        Assert.Null(result.AutoBackupPath);
        Assert.True(File.Exists(db.Paths.DatabasePath));
    }

    [Fact]
    public async Task NextRun_BacksUp_AndPurgesOldDeleted()
    {
        using var db = new TestDb();
        await db.Initializer.InitializeAsync(_ct);
        var e = await db.Entries.AddAsync([new EntryDraft(new DateOnly(2026, 11, 10), new TimeRange(0, 60), "")], _ct);
        await db.Entries.SoftDeleteAsync([e[0].Id], _ct);
        db.Time.Advance(TimeSpan.FromDays(31));

        var result = await db.Initializer.InitializeAsync(_ct);

        Assert.NotNull(result.AutoBackupPath);
        Assert.Equal(1, result.PurgedEntries);
        Assert.Equal(0, result.Migration.Applied);
    }

    [Fact]
    public async Task CorruptFile_ThrowsFriendlyError_AndKeepsFile()
    {
        using var db = new TestDb();
        var garbage = "это не база данных SQLite, а просто текст — его нельзя удалять"u8.ToArray();
        var padded = garbage.Concat(new byte[4096]).ToArray();
        await File.WriteAllBytesAsync(db.Paths.DatabasePath, padded, _ct);

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => db.Initializer.InitializeAsync(_ct));

        Assert.Equal(db.Paths.DatabasePath, ex.DatabasePath);
        Assert.Contains(db.Paths.DatabasePath, ex.Message, StringComparison.Ordinal);
        Assert.Equal(padded, await File.ReadAllBytesAsync(db.Paths.DatabasePath, _ct));
    }

    [Fact]
    public async Task TooNewDatabase_ThrowsFriendlyError()
    {
        using var db = await TestDb.CreateMigratedAsync();
        using (var c = db.Factory.Open())
        {
            c.Execute("INSERT INTO schema_version (version, applied_at) VALUES (999, 'x')");
        }

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => db.Initializer.InitializeAsync(_ct));
        Assert.IsType<DatabaseTooNewException>(ex.InnerException);
    }
}
