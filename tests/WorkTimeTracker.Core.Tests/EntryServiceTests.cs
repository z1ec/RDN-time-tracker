using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using static WorkTimeTracker.Core.Tests.TestClock;

namespace WorkTimeTracker.Core.Tests;

/// <summary>Сценарии F1, F2, F5, F6.3, F7.1 на хранилище в памяти.</summary>
public class EntryServiceTests
{
    private readonly InMemoryEntryRepository _repo = new();
    private readonly EntryService _service;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public EntryServiceTests() => _service = new EntryService(_repo, Create());

    [Fact]
    public async Task AddFromText_SingleLine()
    {
        var r = await _service.AddFromTextAsync("0900 1800 работа", Today, _ct);
        Assert.True(r.IsSuccess);
        Assert.Equal("работа", Assert.Single(_repo.Active).Comment);
    }

    [Fact]
    public async Task AddFromText_Empty_DoesNothing()
    {
        var r = await _service.AddFromTextAsync("   ", Today, _ct);
        Assert.True(r.IsSuccess);
        Assert.Empty(r.Entries);
        Assert.Empty(_repo.Active);
    }

    [Fact]
    public async Task AddFromText_Multiline_IsAtomic()
    {
        var r = await _service.AddFromTextAsync("0900 1300\n1400 1800\n2500 2600", Today, _ct);
        Assert.False(r.IsSuccess);
        Assert.Equal("Строка 3: Часы должны быть от 0 до 23 — «2500»", Assert.Single(r.Errors));
        Assert.Empty(_repo.Active);
    }

    [Fact]
    public async Task AddFromText_Multiline_Success()
    {
        var r = await _service.AddFromTextAsync("0900 1300\n1400 1800", Today, _ct);
        Assert.True(r.IsSuccess);
        Assert.Equal(2, _repo.Active.Count());
    }

    [Fact]
    public async Task AddFromText_OverlapWithExisting()
    {
        await _service.AddFromTextAsync("1300 1500 обед", Today, _ct);
        var r = await _service.AddFromTextAsync("1400 1600", Today, _ct);
        Assert.Equal("Пересекается с записью 10.11 13:00–15:00 «обед»", Assert.Single(r.Errors));
        Assert.Single(_repo.Active);
    }

    [Fact]
    public async Task AddFromText_OverlapInsideBatch_IsDetected()
    {
        var r = await _service.AddFromTextAsync("0900 1300\n1200 1400", Today, _ct);
        Assert.Equal("Строка 2: Пересекается с записью 10.11 09:00–13:00", Assert.Single(r.Errors));
        Assert.Empty(_repo.Active);
    }

    [Fact]
    public async Task AddFromText_NightShift_OverlapsNextDay()
    {
        await _service.AddFromTextAsync("2200 0200", Today.AddDays(-1), _ct);
        var r = await _service.AddFromTextAsync("0100 0300", Today, _ct);
        Assert.False(r.IsSuccess);
        Assert.Equal("Пересекается с записью 09.11 22:00–02:00", r.Errors[0]);
    }

    [Fact]
    public async Task Form_UsesSameRules()
    {
        var draft = _service.BuildDraft(Today, TimeSpan.FromHours(22), TimeSpan.FromHours(2), " ночь ");
        Assert.True(draft.IsSuccess);
        Assert.Equal(1560, draft.Value!.Range.EndMinute);
        Assert.Equal("ночь", draft.Value.Comment);

        Assert.Equal("Нулевая длительность", _service.BuildDraft(Today, TimeSpan.FromHours(9), TimeSpan.FromHours(9), null).Error);
        Assert.Equal("Дата в будущем", _service.BuildDraft(Today.AddDays(1), TimeSpan.FromHours(9), TimeSpan.FromHours(10), null).Error);
        Assert.Equal("Часы должны быть от 0 до 23", _service.BuildDraft(Today, TimeSpan.FromHours(25), TimeSpan.FromHours(10), null).Error);

        var added = await _service.AddAsync(draft.Value, _ct);
        Assert.True(added.IsSuccess);
        var again = await _service.AddAsync(draft.Value, _ct);
        Assert.StartsWith("Пересекается с записью", again.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Form_MidnightToMidnight_IsFullDay()
    {
        var draft = _service.BuildDraft(Today, TimeSpan.Zero, TimeSpan.Zero, "дежурство");
        Assert.True(draft.IsSuccess);
        Assert.Equal((0, 1440), (draft.Value!.Range.StartMinute, draft.Value.Range.EndMinute));
    }

    [Fact]
    public async Task Add_RevalidatesDraft()
    {
        var future = new EntryDraft(Today.AddDays(2), new TimeRange(0, 10), "");
        Assert.Equal("Дата в будущем", (await _service.AddAsync(future, _ct)).Errors[0]);
        Assert.Equal("Дата в будущем", (await _service.UpdateAsync(1, future, _ct)).Errors[0]);
    }

    [Fact]
    public async Task Update_ExcludesItself_AndChecksOthers()
    {
        await _service.AddFromTextAsync("0900 1200 a\n1300 1500 b", Today, _ct);
        var a = _repo.Active.First();

        var ok = await _service.UpdateAsync(a.Id, new EntryDraft(Today, new TimeRange(480, 780), "a2"), _ct);
        Assert.True(ok.IsSuccess);
        Assert.Equal("a2", (await _repo.GetAsync(a.Id, _ct))!.Comment);

        var bad = await _service.UpdateAsync(a.Id, new EntryDraft(Today, new TimeRange(480, 800), "a3"), _ct);
        Assert.Equal("Пересекается с записью 10.11 13:00–15:00 «b»", bad.Errors[0]);
    }

    [Fact]
    public async Task Update_Missing_ReportsError()
    {
        var r = await _service.UpdateAsync(999, new EntryDraft(Today, new TimeRange(0, 10), ""), _ct);
        Assert.Equal("Запись не найдена — возможно, она уже удалена", r.Errors[0]);
    }

    [Fact]
    public async Task Delete_And_Undo()
    {
        var added = (await _service.AddFromTextAsync("0900 1000\n1100 1200\n1300 1400", Today, _ct)).Entries;
        var ids = added.Take(2).Select(e => e.Id).ToArray();

        Assert.Equal(2, await _service.DeleteAsync(ids, _ct));
        Assert.Single(_repo.Active);
        Assert.Equal(2, await _service.UndoDeleteAsync(ids, _ct));
        Assert.Equal(3, _repo.Active.Count());
        Assert.Equal(0, await _service.DeleteAsync([], _ct));
        Assert.Equal(0, await _service.UndoDeleteAsync([], _ct));
    }

    [Fact]
    public void Today_And_Parser_Exposed()
    {
        Assert.Equal(Today, _service.Today);
        Assert.NotNull(_service.Parser);
    }

    [Fact]
    public void OperationResult_Helpers()
    {
        Assert.False(EntryOperationResult.Fail("x").IsSuccess);
        Assert.True(EntryOperationResult.Ok([]).IsSuccess);
    }
}
