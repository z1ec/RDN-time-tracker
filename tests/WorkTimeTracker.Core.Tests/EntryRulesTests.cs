using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.Core.Tests;

public class EntryRulesTests
{
    private static readonly DateOnly Today = TestClock.Today;

    [Fact]
    public void CreateDraft_TrimsComment()
    {
        var r = EntryRules.CreateDraft(Today, 540, 1080, "  текст  ", Today);
        Assert.True(r.IsSuccess);
        Assert.Equal("текст", r.Value!.Comment);
    }

    [Fact]
    public void CreateDraft_NullComment_IsEmpty() => Assert.Equal("", EntryRules.CreateDraft(Today, 540, 1080, null, Today).Value!.Comment);

    [Fact]
    public void CreateDraft_FutureDate() => Assert.Equal("Дата в будущем", EntryRules.CreateDraft(Today.AddDays(1), 540, 1080, "", Today).Error);

    [Fact]
    public void CreateDraft_Zero() => Assert.Equal("Нулевая длительность", EntryRules.CreateDraft(Today, 540, 540, "", Today).Error);

    [Fact]
    public void CreateDraft_NightShift() => Assert.Equal(1560, EntryRules.CreateDraft(Today, 1320, 120, "", Today).Value!.Range.EndMinute);

    [Fact]
    public void Validate_Draft()
    {
        Assert.Null(EntryRules.Validate(new EntryDraft(Today, new TimeRange(0, 10), ""), Today));
        Assert.Equal("Дата в будущем", EntryRules.Validate(new EntryDraft(Today.AddDays(1), new TimeRange(0, 10), ""), Today));
        Assert.Equal("Комментарий длиннее 500 символов", EntryRules.Validate(new EntryDraft(Today, new TimeRange(0, 10), new string('a', 501)), Today));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1440, 1500)]
    public void TimeRange_InvalidStart_Throws(int start, int end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(start, end));

    [Theory]
    [InlineData(100, 100)]
    [InlineData(100, 50)]
    [InlineData(0, 1441)]
    public void TimeRange_InvalidEnd_Throws(int start, int end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(start, end));

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1440, 10)]
    [InlineData(10, -1)]
    [InlineData(10, 1441)]
    public void TimeRange_FromClock_OutOfRange_Throws(int start, int end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeRange.FromClock(start, end));

    [Fact]
    public void WorkEntry_DerivedProperties()
    {
        var e = new WorkEntry(1, Today, new TimeRange(1320, 1560), "ночь");
        Assert.Equal(new TimeOnly(22, 0), e.Start);
        Assert.Equal(new TimeOnly(2, 0), e.End);
        Assert.True(e.EndsNextDay);
        Assert.Equal(TimeSpan.FromHours(4), e.Duration);
        Assert.Equal(new TimeRange(1320, 1560), e.Range);
        Assert.Equal(TimeSpan.FromHours(4), e.Range.Duration);
        Assert.Equal(240, e.AbsoluteEnd - e.AbsoluteStart);
    }

    [Fact]
    public void Result_Ok_And_Fail()
    {
        Assert.True(Result<int>.Ok(5).IsSuccess);
        Assert.Equal("x", Result<int>.Fail("x").Error);
    }
}
