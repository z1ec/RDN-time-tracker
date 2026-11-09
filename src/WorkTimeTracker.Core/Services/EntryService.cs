using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Parsing;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.Core.Services;

/// <summary>Результат добавления или изменения: записи или список ошибок для пользователя.</summary>
public sealed record EntryOperationResult(IReadOnlyList<WorkEntry> Entries, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0;

    public static EntryOperationResult Fail(params string[] errors) => new([], errors);

    public static EntryOperationResult Ok(IReadOnlyList<WorkEntry> entries) => new(entries, []);
}

/// <summary>
/// Сценарии работы с записями: добавление из быстрого ввода (F1) и формы (F2), редактирование (F6.3),
/// удаление с отменой (F5). Одна точка валидации: <see cref="EntryRules"/> + <see cref="EntryValidator"/>.
/// </summary>
public sealed class EntryService(IEntryRepository repository, TimeProvider time)
{
    private readonly QuickEntryParser _parser = new(time);

    public QuickEntryParser Parser => _parser;

    public DateOnly Today => _parser.Today;

    /// <summary>Добавляет записи из текста (одна или несколько строк). Атомарно: ошибка в любой строке — ничего не добавляется.</summary>
    public async Task<EntryOperationResult> AddFromTextAsync(string? text, DateOnly selectedDate, CancellationToken ct = default)
    {
        var batch = _parser.ParseMany(text, selectedDate);
        if (batch.IsEmpty)
        {
            return EntryOperationResult.Ok([]);
        }

        if (!batch.IsSuccess)
        {
            return new EntryOperationResult([], batch.ErrorMessages);
        }

        var multi = batch.NonEmptyLineCount > 1;
        var errors = await CheckOverlapsAsync(
            [.. batch.Lines.Select(l => (multi ? $"Строка {l.LineNumber}: " : string.Empty, l.Draft))], excludeId: null, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return new EntryOperationResult([], errors);
        }

        var added = await repository.AddAsync([.. batch.Lines.Select(l => l.Draft)], ct).ConfigureAwait(false);
        return EntryOperationResult.Ok(added);
    }

    /// <summary>
    /// Строит черновик из значений формы (F2) и диалога редактирования (F6.3) — те же правила, что у быстрого ввода.
    /// В TimePicker нет «24:00», поэтому 00:00–00:00 означает целые сутки (как <c>0000 2400</c>).
    /// </summary>
    public Result<EntryDraft> BuildDraft(DateOnly date, TimeSpan start, TimeSpan end, string? comment)
    {
        var startMinute = (int)start.TotalMinutes;
        var endMinute = (int)end.TotalMinutes;
        if (startMinute is < 0 or >= TimeRange.MinutesPerDay || endMinute is < 0 or > TimeRange.MinutesPerDay)
        {
            return Result<EntryDraft>.Fail(QuickEntryParser.Messages.HoursRange);
        }

        if (startMinute == 0 && endMinute == 0)
        {
            endMinute = TimeRange.MinutesPerDay;
        }

        return EntryRules.CreateDraft(date, startMinute, endMinute, comment, Today);
    }

    /// <summary>Добавляет одну запись (форма F2).</summary>
    public async Task<EntryOperationResult> AddAsync(EntryDraft draft, CancellationToken ct = default)
    {
        var validation = Revalidate(draft);
        if (validation is not null)
        {
            return EntryOperationResult.Fail(validation);
        }

        var errors = await CheckOverlapsAsync([(string.Empty, draft)], excludeId: null, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return new EntryOperationResult([], errors);
        }

        return EntryOperationResult.Ok(await repository.AddAsync([draft], ct).ConfigureAwait(false));
    }

    /// <summary>Изменяет запись (F6.3): та же валидация, пересечения — без учёта самой записи.</summary>
    public async Task<EntryOperationResult> UpdateAsync(long id, EntryDraft draft, CancellationToken ct = default)
    {
        var validation = Revalidate(draft);
        if (validation is not null)
        {
            return EntryOperationResult.Fail(validation);
        }

        var errors = await CheckOverlapsAsync([(string.Empty, draft)], excludeId: id, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return new EntryOperationResult([], errors);
        }

        var entry = draft.ToEntry(id);
        return await repository.UpdateAsync(entry, ct).ConfigureAwait(false)
            ? EntryOperationResult.Ok([entry])
            : EntryOperationResult.Fail("Запись не найдена — возможно, она уже удалена");
    }

    /// <summary>Мягко удаляет записи (F5.3). Возвращает число удалённых.</summary>
    public Task<int> DeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        ids.Count == 0 ? Task.FromResult(0) : repository.SoftDeleteAsync(ids, ct);

    /// <summary>Отменяет удаление (F5.3).</summary>
    public Task<int> UndoDeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        ids.Count == 0 ? Task.FromResult(0) : repository.RestoreAsync(ids, ct);

    /// <summary>Повторная проверка готового черновика (дата и комментарий; интервал валиден по построению).</summary>
    private string? Revalidate(EntryDraft draft) => EntryRules.Validate(draft, Today);

    private async Task<List<string>> CheckOverlapsAsync(IReadOnlyList<(string Prefix, EntryDraft Draft)> drafts, long? excludeId, CancellationToken ct)
    {
        var from = drafts.Min(d => d.Draft.Date).AddDays(-EntryValidator.NeighbourDays);
        var to = drafts.Max(d => d.Draft.Date).AddDays(EntryValidator.NeighbourDays);
        var existing = (await repository.GetRangeAsync(from, to, ct).ConfigureAwait(false)).ToList();
        var errors = new List<string>();

        // Новые записи проверяются и с существующими, и друг с другом (id < 0 — временные).
        long tempId = -1;
        foreach (var (prefix, draft) in drafts)
        {
            var error = EntryValidator.Validate(draft, existing, excludeId);
            if (error is not null)
            {
                errors.Add(prefix + error);
            }

            existing.Add(draft.ToEntry(tempId--));
        }

        return errors;
    }
}
