namespace WorkTimeTracker.Core.Model;

/// <summary>
/// Лист блокнота (F8): рабочий лист поверх общих записей. Текущий лист — тот, у которого
/// <see cref="ClearedAt"/> пуст; после «Очистить всё» лист уходит в архив (ADR-0009).
/// </summary>
public sealed record NotepadSheet(long Id, string Title, DateTimeOffset StartedAt, DateTimeOffset? ClearedAt)
{
    public const int MaxTitleLength = 100;

    public bool IsCurrent => ClearedAt is null;
}

/// <summary>
/// Лист вместе с его видимыми записями: привязка не убрана кнопкой ✕ и запись не удалена.
/// Записи — текущее состояние из журнала, а не снимок (F8.16), по возрастанию даты и начала (F8.8).
/// </summary>
public sealed record NotepadSheetView(NotepadSheet Sheet, IReadOnlyList<WorkEntry> Entries)
{
    /// <summary>Сумма считается как в статистике: длительность записи целиком, ночная смена не делится (F8.6).</summary>
    public int TotalMinutes => Entries.Sum(e => e.DurationMinutes);

    public bool IsEmpty => Entries.Count == 0;

    public DateOnly? FirstDate => IsEmpty ? null : Entries.Min(e => e.Date);

    public DateOnly? LastDate => IsEmpty ? null : Entries.Max(e => e.Date);
}

/// <summary>Сколько строк физически удалила очистка мусора блокнота (F8.18).</summary>
public sealed record NotepadPurgeResult(int Items, int Sheets);
