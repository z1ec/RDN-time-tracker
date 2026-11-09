using Dapper;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data.Tests;

/// <summary>F7.4, F4.8: настройки.</summary>
public class SettingsStoreTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Defaults_WhenEmpty()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var s = await db.Settings.LoadAsync(_ct);
        Assert.Equal(480, s.DailyNormMinutes);
        Assert.Equal(AppSettings.DefaultWorkDays.Order(), s.WorkDays.Order());
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.Null(s.Window);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        using var db = await TestDb.CreateMigratedAsync();
        var saved = new AppSettings
        {
            DailyNormMinutes = 450,
            WorkDays = new HashSet<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday },
            Theme = ThemePreference.Dark,
            Window = new WindowPlacement(10, -20, 1300, 800, true),
        };
        await db.Settings.SaveAsync(saved, _ct);
        await db.Settings.SaveAsync(saved, _ct); // повторное сохранение — upsert

        var loaded = await db.Settings.LoadAsync(_ct);
        Assert.Equal(450, loaded.DailyNormMinutes);
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Monday], loaded.WorkDays.Order());
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(saved.Window, loaded.Window);

        await db.Settings.SaveAsync(saved with { Window = null, WorkDays = new HashSet<DayOfWeek>() }, _ct);
        loaded = await db.Settings.LoadAsync(_ct);
        Assert.Null(loaded.Window);
        Assert.Empty(loaded.WorkDays);
    }

    [Theory]
    [InlineData("daily_norm_minutes", "abc")]
    [InlineData("daily_norm_minutes", "5000")]
    [InlineData("work_days", "1,9")]
    [InlineData("work_days", "x")]
    [InlineData("theme", "Purple")]
    [InlineData("theme", "42")]
    [InlineData("window", "1,2,3")]
    [InlineData("window", "1,2,a,4,0")]
    [InlineData("window", "1,2,0,0,0")]
    public async Task CorruptedValues_FallBackToDefaults(string key, string value)
    {
        using var db = await TestDb.CreateMigratedAsync();
        using (var c = db.Factory.Open())
        {
            c.Execute("INSERT INTO settings (key, value) VALUES (@key, @value)", new { key, value });
        }

        var s = await db.Settings.LoadAsync(_ct);
        Assert.Equal(480, s.DailyNormMinutes);
        Assert.Equal(5, s.WorkDays.Count);
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.Null(s.Window);
    }
}
