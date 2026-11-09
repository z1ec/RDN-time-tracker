using Dapper;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F4.5: резервные копии и ротация.</summary>
public class BackupServiceTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task BackupNow_CreatesReadableCopy()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Entries.AddAsync([new EntryDraft(new DateOnly(2026, 11, 10), new TimeRange(540, 1080), "в копии")], _ct);

        var path = await db.Backups.BackupNowAsync(_ct);

        Assert.Equal(Path.Combine(db.Paths.BackupsDirectory, "worktime-2026-11-10.db"), path);
        var copy = new EntryRepository(new SqliteConnectionFactory(path), db.Time);
        Assert.Equal("в копии", Assert.Single(await copy.GetAllAsync(_ct)).Comment);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task AutoBackup_OncePerDay()
    {
        using var db = await TestDb.CreateMigratedAsync();
        Assert.NotNull(await db.Backups.AutoBackupAsync(_ct));
        Assert.Null(await db.Backups.AutoBackupAsync(_ct));
        db.Time.Advance(TimeSpan.FromDays(1));
        Assert.EndsWith("worktime-2026-11-11.db", await db.Backups.AutoBackupAsync(_ct), StringComparison.Ordinal);
        Assert.Equal(2, db.Backups.ListBackups().Count);
    }

    [Fact]
    public async Task Rotation_Keeps14Newest()
    {
        using var db = await TestDb.CreateMigratedAsync();
        for (var i = 0; i < 20; i++)
        {
            await db.Backups.BackupNowAsync(_ct);
            db.Time.Advance(TimeSpan.FromDays(1));
        }

        var list = db.Backups.ListBackups();
        Assert.Equal(BackupService.KeepCount, list.Count);
        Assert.EndsWith("worktime-2026-11-29.db", list[0], StringComparison.Ordinal);
        Assert.EndsWith("worktime-2026-11-16.db", list[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ListBackups_NoFolder_IsEmpty()
    {
        using var dir = new TempDirectory();
        var paths = new DataPaths(dir.Combine("nope"), DataLocationKind.CommandLine);
        var service = new BackupService(new SqliteConnectionFactory(paths.DatabasePath), paths, TimeProvider.System);
        Assert.Empty(service.ListBackups());
        Assert.Equal(0, service.Rotate());
    }

    [Fact]
    public async Task Backup_DoesNotLockSourceForWrites()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Backups.BackupNowAsync(_ct);
        await db.Entries.AddAsync([new EntryDraft(new DateOnly(2026, 11, 10), new TimeRange(0, 60), "")], _ct);
        using var c = db.Factory.Open();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT count(*) FROM entries"));
    }
}
