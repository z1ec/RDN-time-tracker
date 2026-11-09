using Dapper;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F4.1, F5.3, F6.4: CRUD, мягкое удаление, очистка, поиск по кириллице, конкурентная запись.</summary>
public class EntryRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static EntryDraft Draft(DateOnly date, int start, int end, string comment = "") => new(date, new TimeRange(start, end), comment);

    [Fact]
    public async Task Add_Get_Range_All()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await db.Entries.AddAsync(
            [Draft(Today, 540, 1080, "день"), Draft(Today.AddDays(-1), 1320, 1560, "ночь"), Draft(Today.AddDays(-5), 600, 700)], _ct);

        Assert.Equal(3, added.Count);
        Assert.All(added, e => Assert.True(e.Id > 0));
        Assert.Equal(added[0], await db.Entries.GetAsync(added[0].Id, _ct));
        Assert.Null(await db.Entries.GetAsync(999, _ct));

        var range = await db.Entries.GetRangeAsync(Today.AddDays(-1), Today, _ct);
        Assert.Equal(["ночь", "день"], range.Select(e => e.Comment));
        Assert.Equal(1560, range[0].EndMinute);

        Assert.Equal(3, (await db.Entries.GetAllAsync(_ct)).Count);
        Assert.Equal(Today.AddDays(-5), await db.Entries.GetFirstDateAsync(_ct));
    }

    [Fact]
    public async Task Empty_FirstDate_IsNull()
    {
        using var db = await TestDb.CreateMigratedAsync();
        Assert.Null(await db.Entries.GetFirstDateAsync(_ct));
        Assert.Empty(await db.Entries.AddAsync([], _ct));
    }

    [Fact]
    public async Task Data_SurvivesReopen()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Entries.AddAsync([Draft(Today, 540, 1080, "сохранено")], _ct);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var reopened = new EntryRepository(new SqliteConnectionFactory(db.Paths.DatabasePath), db.Time);
        Assert.Equal("сохранено", Assert.Single(await reopened.GetAllAsync(_ct)).Comment);
    }

    [Fact]
    public async Task Add_IsAtomic()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var tooLong = new string('ж', 501); // нарушает CHECK (length(comment) <= 500)
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            db.Entries.AddAsync([Draft(Today, 0, 60), Draft(Today, 60, 120, tooLong)], _ct));
        Assert.Empty(await db.Entries.GetAllAsync(_ct));
    }

    [Fact]
    public async Task Timestamps_AreUtcIso()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var e = (await db.Entries.AddAsync([Draft(Today, 0, 60)], _ct))[0];
        db.Time.Advance(TimeSpan.FromMinutes(5));
        await db.Entries.UpdateAsync(e with { Comment = "x" }, _ct);

        using var c = db.Factory.Open();
        var row = c.QuerySingle<(string Created, string Updated)>("SELECT created_at, updated_at FROM entries");
        Assert.Equal("2026-11-10T14:37:00.000Z", row.Created);
        Assert.Equal("2026-11-10T14:42:00.000Z", row.Updated);
    }

    [Fact]
    public async Task Update()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var e = (await db.Entries.AddAsync([Draft(Today, 540, 1080, "a")], _ct))[0];

        Assert.True(await db.Entries.UpdateAsync(e with { Date = Today.AddDays(-1), StartMinute = 600, EndMinute = 1500, Comment = "b" }, _ct));
        var updated = (await db.Entries.GetAsync(e.Id, _ct))!;
        Assert.Equal((Today.AddDays(-1), 600, 1500, "b"), (updated.Date, updated.StartMinute, updated.EndMinute, updated.Comment));
        Assert.False(await db.Entries.UpdateAsync(e with { Id = 12345 }, _ct));
    }

    [Fact]
    public async Task SoftDelete_HidesEverywhere_AndRestoreBringsBack()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await db.Entries.AddAsync([Draft(Today, 0, 60, "созвон"), Draft(Today, 60, 120), Draft(Today, 120, 180)], _ct);
        var ids = new[] { added[0].Id, added[1].Id };

        Assert.Equal(2, await db.Entries.SoftDeleteAsync(ids, _ct));
        Assert.Equal(0, await db.Entries.SoftDeleteAsync(ids, _ct)); // повторно — ничего
        Assert.Single(await db.Entries.GetAllAsync(_ct));
        Assert.Single(await db.Entries.GetRangeAsync(Today, Today, _ct));
        Assert.Null(await db.Entries.GetAsync(added[0].Id, _ct));
        Assert.Empty(await db.Entries.SearchAsync("созвон", _ct));
        Assert.False(await db.Entries.UpdateAsync(added[0], _ct));

        using (var c = db.Factory.Open())
        {
            Assert.Equal(3, c.ExecuteScalar<int>("SELECT count(*) FROM entries")); // физически на месте
        }

        Assert.Equal(2, await db.Entries.RestoreAsync(ids, _ct));
        Assert.Equal(0, await db.Entries.RestoreAsync(ids, _ct));
        Assert.Equal(3, (await db.Entries.GetAllAsync(_ct)).Count);
        Assert.Equal(0, await db.Entries.SoftDeleteAsync([], _ct));
    }

    [Fact]
    public async Task Purge_RemovesOnlyOldDeleted()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var added = await db.Entries.AddAsync([Draft(Today, 0, 60), Draft(Today, 60, 120), Draft(Today, 120, 180)], _ct);
        await db.Entries.SoftDeleteAsync([added[0].Id], _ct);           // удалена 31 день назад (см. ниже)
        db.Time.Advance(TimeSpan.FromDays(2));
        await db.Entries.SoftDeleteAsync([added[1].Id], _ct);           // удалена 29 дней назад
        db.Time.Advance(TimeSpan.FromDays(29));

        var purged = await db.Entries.PurgeDeletedAsync(db.Time.GetUtcNow() - TimeSpan.FromDays(30), _ct);

        Assert.Equal(1, purged);
        using var c = db.Factory.Open();
        Assert.Equal(new[] { added[1].Id, added[2].Id }, c.Query<long>("SELECT id FROM entries ORDER BY id"));
    }

    [Theory]
    [InlineData("созвон")]
    [InlineData("СОЗВОН")]
    [InlineData("Созвон")]
    [InlineData("  звон ")]
    public async Task Search_IsCaseInsensitive_ForCyrillic(string query)
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Entries.AddAsync([Draft(Today, 0, 60, "Созвон с клиентом"), Draft(Today, 60, 120, "обед"), Draft(Today, 120, 180, "созвон")], _ct);

        var found = await db.Entries.SearchAsync(query, _ct);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Search_Latin_And_Empty()
    {
        using var db = await TestDb.CreateMigratedAsync();
        await db.Entries.AddAsync([Draft(Today, 0, 60, "Code Review"), Draft(Today, 60, 120, ""), Draft(Today, 120, 180, "ревью")], _ct);
        Assert.Single(await db.Entries.SearchAsync("code", _ct));
        Assert.Equal(2, (await db.Entries.SearchAsync(" ", _ct)).Count); // пустой запрос — все с комментарием
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotCorruptDatabase()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var tasks = Enumerable.Range(0, 40).Select(i =>
            db.Entries.AddAsync([Draft(Today.AddDays(-i), 0, 60, $"запись {i}")], _ct));
        await Task.WhenAll(tasks);

        Assert.Equal(40, (await db.Entries.GetAllAsync(_ct)).Count);
        using var c = db.Factory.Open();
        Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
    }

    [Fact]
    public async Task Schema_RejectsInvalidRows()
    {
        using var db = await TestDb.CreateMigratedAsync();
        using var c = db.Factory.Open();
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            c.Execute("INSERT INTO entries (work_date, start_min, end_min, created_at, updated_at) VALUES ('2026-11-10', 600, 600, 'x', 'x')"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            c.Execute("INSERT INTO entries (work_date, start_min, end_min, created_at, updated_at) VALUES ('2026-11-10', 0, 1441, 'x', 'x')"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            c.Execute("INSERT INTO entries (work_date, start_min, end_min, created_at, updated_at) VALUES ('2026-11-10', 1440, 1500, 'x', 'x')"));
    }
}
