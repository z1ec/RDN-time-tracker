using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Tests;

/// <summary>Блокнот в памяти поверх <see cref="InMemoryEntryRepository"/> — для тестов сервисов Core.</summary>
public sealed class InMemoryNotepadRepository(InMemoryEntryRepository entries, TimeProvider time) : INotepadRepository
{
    private readonly List<Sheet> _sheets = [];
    private long _nextId = 1;

    private Sheet? Current => _sheets.SingleOrDefault(s => s.ClearedAt is null && s.DeletedAt is null);

    public async Task<IReadOnlyList<WorkEntry>> AddEntriesAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default)
    {
        var sheet = Current ?? Create(string.Empty);
        var added = await entries.AddAsync(drafts, ct);
        foreach (var e in added)
        {
            sheet.Items[e.Id] = false;
        }

        return added;
    }

    public Task<NotepadSheetView?> GetCurrentAsync(CancellationToken ct = default) =>
        Task.FromResult(Current is { } s ? View(s) : null);

    public Task<IReadOnlyList<NotepadSheetView>> GetArchiveAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotepadSheetView>>(
            [.. _sheets.Where(s => s.ClearedAt is not null && s.DeletedAt is null).OrderByDescending(s => s.ClearedAt).ThenByDescending(s => s.Id).Select(View)]);

    public Task SetTitleAsync(string title, CancellationToken ct = default)
    {
        if (Current is { } s)
        {
            s.Title = title;
        }
        else if (title.Length > 0)
        {
            Create(title);
        }

        return Task.CompletedTask;
    }

    public Task<int> RemoveItemsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default) =>
        Task.FromResult(Current is { } s ? SetRemoved([s], entryIds, true) : 0);

    public Task<int> RestoreItemsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default) =>
        Task.FromResult(SetRemoved(_sheets, entryIds, false));

    public Task<long?> ClearCurrentAsync(CancellationToken ct = default)
    {
        var s = Current;
        if (s is not null)
        {
            s.ClearedAt = time.GetUtcNow();
        }

        return Task.FromResult(s?.Id);
    }

    public Task<bool> UndoClearAsync(long sheetId, CancellationToken ct = default)
    {
        var s = _sheets.SingleOrDefault(x => x.Id == sheetId && x.ClearedAt is not null && x.DeletedAt is null);
        if (s is null || Current is not null)
        {
            return Task.FromResult(false);
        }

        s.ClearedAt = null;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteSheetAsync(long sheetId, CancellationToken ct = default)
    {
        var s = _sheets.SingleOrDefault(x => x.Id == sheetId && x.ClearedAt is not null && x.DeletedAt is null);
        s?.DeletedAt = time.GetUtcNow();
        return Task.FromResult(s is not null);
    }

    public Task<bool> RestoreSheetAsync(long sheetId, CancellationToken ct = default)
    {
        var s = _sheets.SingleOrDefault(x => x.Id == sheetId && x.DeletedAt is not null);
        s?.DeletedAt = null;
        return Task.FromResult(s is not null);
    }

    public Task<NotepadPurgeResult> PurgeAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default) =>
        Task.FromResult(new NotepadPurgeResult(0, _sheets.RemoveAll(s => s.DeletedAt < olderThanUtc)));

    private static int SetRemoved(IEnumerable<Sheet> sheets, IReadOnlyCollection<long> ids, bool removed)
    {
        var n = 0;
        foreach (var s in sheets)
        {
            foreach (var id in ids.Where(id => s.Items.TryGetValue(id, out var r) && r != removed))
            {
                s.Items[id] = removed;
                n++;
            }
        }

        return n;
    }

    private Sheet Create(string title)
    {
        var s = new Sheet { Id = _nextId++, Title = title, StartedAt = time.GetUtcNow() };
        _sheets.Add(s);
        return s;
    }

    private NotepadSheetView View(Sheet s)
    {
        var visible = s.Items.Where(i => !i.Value).Select(i => i.Key).ToHashSet();
        return new NotepadSheetView(
            new NotepadSheet(s.Id, s.Title, s.StartedAt, s.ClearedAt),
            [.. entries.Active.Where(e => visible.Contains(e.Id)).OrderBy(e => e.Date).ThenBy(e => e.StartMinute)]);
    }

    private sealed class Sheet
    {
        public long Id { get; init; }

        public string Title { get; set; } = "";

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? ClearedAt { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }

        /// <summary>Запись → убрана ли из листа.</summary>
        public Dictionary<long, bool> Items { get; } = [];
    }
}
