using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Tests;

/// <summary>Простая реализация хранилища в памяти для тестов сервисов Core.</summary>
public sealed class InMemoryEntryRepository : IEntryRepository
{
    private readonly Dictionary<long, (WorkEntry Entry, bool Deleted)> _items = [];
    private long _nextId = 1;

    public IEnumerable<WorkEntry> Active => _items.Values.Where(v => !v.Deleted).Select(v => v.Entry);

    public Task<IReadOnlyList<WorkEntry>> AddAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default)
    {
        var added = drafts.Select(d => d.ToEntry(_nextId++)).ToList();
        foreach (var e in added)
        {
            _items[e.Id] = (e, false);
        }

        return Task.FromResult<IReadOnlyList<WorkEntry>>(added);
    }

    public Task<bool> UpdateAsync(WorkEntry entry, CancellationToken ct = default)
    {
        if (!_items.TryGetValue(entry.Id, out var v) || v.Deleted)
        {
            return Task.FromResult(false);
        }

        _items[entry.Id] = (entry, false);
        return Task.FromResult(true);
    }

    public Task<WorkEntry?> GetAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_items.TryGetValue(id, out var v) && !v.Deleted ? v.Entry : null);

    public Task<IReadOnlyList<WorkEntry>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WorkEntry>>([.. Sorted(Active.Where(e => e.Date >= from && e.Date <= to))]);

    public Task<IReadOnlyList<WorkEntry>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WorkEntry>>([.. Sorted(Active)]);

    public Task<DateOnly?> GetFirstDateAsync(CancellationToken ct = default) =>
        Task.FromResult(Active.Any() ? Active.Min(e => e.Date) : (DateOnly?)null);

    public Task<IReadOnlyList<WorkEntry>> SearchAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WorkEntry>>([.. Sorted(Active.Where(e => e.Comment.Contains(text, StringComparison.CurrentCultureIgnoreCase)))]);

    public Task<int> SoftDeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) => SetDeleted(ids, true);

    public Task<int> RestoreAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) => SetDeleted(ids, false);

    public Task<int> PurgeDeletedAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default) => Task.FromResult(0);

    private Task<int> SetDeleted(IReadOnlyCollection<long> ids, bool deleted)
    {
        var n = 0;
        foreach (var id in ids)
        {
            if (_items.TryGetValue(id, out var v) && v.Deleted != deleted)
            {
                _items[id] = (v.Entry, deleted);
                n++;
            }
        }

        return Task.FromResult(n);
    }

    private static IEnumerable<WorkEntry> Sorted(IEnumerable<WorkEntry> e) => e.OrderBy(x => x.Date).ThenBy(x => x.StartMinute);
}
