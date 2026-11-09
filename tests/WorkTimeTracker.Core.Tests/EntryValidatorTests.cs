using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.Core.Tests;

/// <summary>F7.1: запрет пересечений, в том числе через полночь.</summary>
public class EntryValidatorTests
{
    private static readonly DateOnly Nov10 = new(2026, 11, 10);
    private static readonly DateOnly Nov11 = new(2026, 11, 11);

    private static EntryDraft Draft(DateOnly date, int start, int end) => new(date, new TimeRange(start, end), "");

    [Theory]
    [InlineData(300, 400, false)]   // до
    [InlineData(1080, 1200, false)] // касание концом существующей
    [InlineData(400, 540, false)]   // касание началом существующей
    [InlineData(500, 600, true)]    // заходит в начало
    [InlineData(1000, 1100, true)]  // заходит в конец
    [InlineData(600, 900, true)]    // внутри
    [InlineData(300, 1300, true)]   // накрывает
    [InlineData(540, 1080, true)]   // совпадает
    public void SameDay(int start, int end, bool overlaps)
    {
        var existing = new[] { new WorkEntry(1, Nov10, 540, 1080, "") };
        var draft = Draft(Nov10, start, end);
        Assert.Equal(overlaps, EntryValidator.FindOverlap(draft, existing) is not null);
    }

    [Fact]
    public void NightShift_OverlapsNextDayMorning()
    {
        var existing = new[] { new WorkEntry(1, Nov10, 1320, 1560, "ночь") }; // 10.11 22:00 – 02:00
        Assert.NotNull(EntryValidator.FindOverlap(Draft(Nov11, 60, 180), existing)); // 11.11 01:00–03:00
        Assert.Null(EntryValidator.FindOverlap(Draft(Nov11, 120, 180), existing));   // касание в 02:00
    }

    [Fact]
    public void NextDayEntry_OverlapsPreviousNightShiftDraft()
    {
        var existing = new[] { new WorkEntry(1, Nov11, 60, 180, "") };
        Assert.NotNull(EntryValidator.FindOverlap(Draft(Nov10, 1320, 1560), existing));
    }

    [Fact]
    public void EditedEntry_IsNotComparedWithItself()
    {
        var existing = new[] { new WorkEntry(7, Nov10, 540, 1080, "") };
        Assert.Null(EntryValidator.FindOverlap(Draft(Nov10, 600, 1100), existing, excludeId: 7));
        Assert.NotNull(EntryValidator.FindOverlap(Draft(Nov10, 600, 1100), existing, excludeId: 8));
    }

    [Fact]
    public void Message_IncludesDateTimeAndComment()
    {
        var existing = new[] { new WorkEntry(1, Nov10, 780, 900, "обед") };
        Assert.Equal("Пересекается с записью 10.11 13:00–15:00 «обед»", EntryValidator.Validate(Draft(Nov10, 840, 960), existing));
    }

    [Fact]
    public void Message_WithoutComment_AndNightShift()
    {
        var existing = new[] { new WorkEntry(1, Nov10, 1320, 1560, "") };
        Assert.Equal("Пересекается с записью 10.11 22:00–02:00", EntryValidator.Validate(Draft(Nov10, 1380, 1400), existing));
    }

    [Fact]
    public void Message_LongComment_IsShortened()
    {
        var existing = new[] { new WorkEntry(1, Nov10, 780, 900, new string('ж', 60)) };
        var message = EntryValidator.Validate(Draft(Nov10, 840, 960), existing)!;
        Assert.EndsWith("…»", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstOverlap_IsEarliest()
    {
        var existing = new[] { new WorkEntry(2, Nov10, 900, 960, "b"), new WorkEntry(1, Nov10, 600, 660, "a") };
        Assert.Equal(1, EntryValidator.FindOverlap(Draft(Nov10, 500, 1000), existing)!.Id);
    }
}
