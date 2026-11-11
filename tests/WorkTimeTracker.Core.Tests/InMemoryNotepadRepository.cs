using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Tests;

/// <summary>Блокнот в памяти поверх <see cref="InMemoryEntryRepository"/> — для тестов сервисов Core.</summary>
public sealed class InMemoryNotepadRepository : INotepadRepository
{
    private readonly InMemoryEntryRepository _entries;
    private readonly TimeProvider _time;
    private readonly List<Sheet> _sheets = [];
    private long _nextSheetId = 1;
    private long _nextLineId = 1;

    public InMemoryNotepadRepository(InMemoryEntryRepository entries, TimeProvider time)
    {
        _entries = entries;
        _time = time;

        // F9.6: правка записи в «Журнале» переносится в связанную строку.
        _entries.Updated += e =>
        {
            foreach (var line in _sheets.SelectMany(s => s.Lines).Where(l => l.EntryId == e.Id))
            {
                line.Values = new EntryDraft(e.Date, e.Range, e.Comment);
            }
        };
    }

    private Sheet? Current => _sheets.SingleOrDefault(s => s.ClearedAt is null && s.DeletedAt is null);

    public async Task<IReadOnlyList<NotepadLine>> AddLinesAsync(IReadOnlyList<NotepadLineDraft> lines, CancellationToken ct = default)
    {
        var sheet = Current ?? Create(string.Empty);
        var result = new List<NotepadLine>();
        foreach (var draft in lines)
        {
            var entry = draft.CreateEntry ? (await _entries.AddAsync([draft.Draft], ct))[0] : null;
            var line = new Line { Id = _nextLineId++, Values = draft.Draft, EntryId = entry?.Id };
            sheet.Lines.Add(line);
            result.Add(ToLine(line)!);
        }

        return result;
    }

    public Task<NotepadLine?> GetLineAsync(long lineId, CancellationToken ct = default) =>
        Task.FromResult(FindCurrent(lineId) is { } l ? ToLine(l) : null);

    public async Task<NotepadLine?> UpdateLineAsync(long lineId, EntryDraft values, NotepadLineUpdate mode, CancellationToken ct = default)
    {
        if (FindCurrent(lineId) is not { } line)
        {
            return null;
        }

        line.Values = values;
        if (mode == NotepadLineUpdate.LineAndEntry && line.EntryId is { } id)
        {
            await _entries.UpdateAsync(values.ToEntry(id), ct);
        }
        else if (mode == NotepadLineUpdate.LineAndNewEntry && line.EntryId is null)
        {
            line.EntryId = (await _entries.AddAsync([values], ct))[0].Id;
        }

        return ToLine(line);
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

    public Task<int> RemoveLinesAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default) =>
        Task.FromResult(Current is { } s ? SetRemoved([s], lineIds, true) : 0);

    public Task<int> RestoreLinesAsync(IReadOnlyCollection<long> lineIds, CancellationToken ct = default) =>
        Task.FromResult(SetRemoved(_sheets, lineIds, false));

    public Task<long?> ClearCurrentAsync(CancellationToken ct = default)
    {
        var s = Current;
        if (s is not null)
        {
            s.ClearedAt = _time.GetUtcNow();
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
        s?.DeletedAt = _time.GetUtcNow();
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
        foreach (var line in sheets.SelectMany(s => s.Lines).Where(l => ids.Contains(l.Id) && l.Removed != removed))
        {
            line.Removed = removed;
            n++;
        }

        return n;
    }

    private Line? FindCurrent(long lineId) => Current?.Lines.SingleOrDefault(l => l.Id == lineId && IsVisible(l));

    private bool IsVisible(Line l) => !l.Removed && (l.EntryId is not { } id || !_entries.IsDeleted(id));

    private NotepadLine? ToLine(Line l)
    {
        var linked = l.EntryId is { } id ? _entries.Active.SingleOrDefault(e => e.Id == id) : null;
        return new NotepadLine(l.Id, l.Values.Date, l.Values.Range.StartMinute, l.Values.Range.EndMinute, l.Values.Comment, linked);
    }

    private Sheet Create(string title)
    {
        var s = new Sheet { Id = _nextSheetId++, Title = title, StartedAt = _time.GetUtcNow() };
        _sheets.Add(s);
        return s;
    }

    private NotepadSheetView View(Sheet s) =>
        new(
            new NotepadSheet(s.Id, s.Title, s.StartedAt, s.ClearedAt),
            [.. s.Lines.Where(IsVisible).OrderBy(l => l.Values.Date).ThenBy(l => l.Values.Range.StartMinute).ThenBy(l => l.Id).Select(l => ToLine(l)!)]);

    private sealed class Sheet
    {
        public long Id { get; init; }

        public string Title { get; set; } = "";

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? ClearedAt { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }

        public List<Line> Lines { get; } = [];
    }

    private sealed class Line
    {
        public long Id { get; init; }

        public required EntryDraft Values { get; set; }

        public long? EntryId { get; set; }

        public bool Removed { get; set; }
    }
}
