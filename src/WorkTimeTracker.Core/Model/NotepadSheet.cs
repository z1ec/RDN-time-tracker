namespace WorkTimeTracker.Core.Model;

/// <summary>
/// Лист блокнота (F8): рабочий лист со своими строками. Текущий лист — тот, у которого
/// <see cref="ClearedAt"/> пуст; после «Очистить всё» лист уходит в архив (ADR-0009).
/// </summary>
public sealed record NotepadSheet(long Id, string Title, DateTimeOffset StartedAt, DateTimeOffset? ClearedAt)
{
    public const int MaxTitleLength = 100;

    public bool IsCurrent => ClearedAt is null;
}

/// <summary>Статус строки блокнота (F9.3). Не хранится — вычисляется из строки и связанной записи.</summary>
public enum NotepadLineStatus
{
    /// <summary>Связана с записью, и дата, время и комментарий совпадают: время учтено в «Журнале» и «Статистике».</summary>
    InStatistics,

    /// <summary>Не связана: при добавлении было пересечение, время есть только в блокноте.</summary>
    NotInStatistics,

    /// <summary>Связана, но значения отличаются: новое время пересеклось с другой записью, в статистике осталось прежнее.</summary>
    DifferentInStatistics,
}

/// <summary>
/// Строка блокнота (F9, ADR-0010): свои дата, время и комментарий. <see cref="Linked"/> — связанная запись журнала
/// в её текущем состоянии или <c>null</c>, если строка не связана («не в статистике»).
/// </summary>
public sealed record NotepadLine(long Id, DateOnly Date, int StartMinute, int EndMinute, string Comment, WorkEntry? Linked)
{
    /// <summary>
    /// Запись базы, с которой строка пересекается на момент показа (для подсказки F9.3); <c>null</c> — пересечения нет
    /// или строка «в статистике». Заполняет <see cref="WorkTimeTracker.Core.Services.NotepadService"/>.
    /// </summary>
    public WorkEntry? Conflict { get; init; }

    public long? EntryId => Linked?.Id;

    public int DurationMinutes => EndMinute - StartMinute;

    public bool EndsNextDay => EndMinute > TimeRange.MinutesPerDay;

    public NotepadLineStatus Status =>
        Linked is null ? NotepadLineStatus.NotInStatistics
        : Matches(Linked) ? NotepadLineStatus.InStatistics
        : NotepadLineStatus.DifferentInStatistics;

    /// <summary>Значения строки в виде записи (с id строки) — для показа и форматирования.</summary>
    public WorkEntry Values => new(Id, Date, StartMinute, EndMinute, Comment);

    public EntryDraft ToDraft() => new(Date, new TimeRange(StartMinute, EndMinute), Comment);

    /// <summary>Совпадают ли дата, время и комментарий строки с записью.</summary>
    public bool Matches(WorkEntry entry) =>
        entry.Date == Date && entry.StartMinute == StartMinute && entry.EndMinute == EndMinute
        && string.Equals(entry.Comment, Comment, StringComparison.Ordinal);
}

/// <summary>
/// Лист вместе с видимыми строками: строка не убрана кнопкой ✕ и, если связана, её запись не удалена в «Журнале».
/// Строки — по возрастанию даты и начала (F8.8). Связанные записи — текущее состояние из журнала (F8.16).
/// </summary>
public sealed record NotepadSheetView(NotepadSheet Sheet, IReadOnlyList<NotepadLine> Lines)
{
    /// <summary>Сумма всех видимых строк, как на калькуляторе (F9.4). Ночная смена не делится.</summary>
    public int TotalMinutes => Lines.Sum(l => l.DurationMinutes);

    /// <summary>Сумма связанных записей — их фактическое время в базе (F9.4).</summary>
    public int StatisticsMinutes => Lines.Where(l => l.Linked is not null).Sum(l => l.Linked!.DurationMinutes);

    /// <summary>Вторая цифра на плашке нужна, только если суммы расходятся (F9.4).</summary>
    public bool HasStatisticsDifference => TotalMinutes != StatisticsMinutes;

    public bool IsEmpty => Lines.Count == 0;

    public DateOnly? FirstDate => IsEmpty ? null : Lines.Min(l => l.Date);

    public DateOnly? LastDate => IsEmpty ? null : Lines.Max(l => l.Date);
}

/// <summary>Новая строка блокнота: значения и решение, записывать ли её в базу (F9.2).</summary>
public sealed record NotepadLineDraft(EntryDraft Draft, bool CreateEntry);

/// <summary>Что делать с базой при правке строки блокнота (F9.5).</summary>
public enum NotepadLineUpdate
{
    /// <summary>Меняется только строка: новое время пересекается с записью базы.</summary>
    LineOnly,

    /// <summary>Строка связана: то же изменение применяется к записи.</summary>
    LineAndEntry,

    /// <summary>Строка не связана, пересечений нет: создаётся запись, и строка связывается с ней.</summary>
    LineAndNewEntry,
}

/// <summary>Сколько строк физически удалила очистка мусора блокнота (F8.18).</summary>
public sealed record NotepadPurgeResult(int Lines, int Sheets);
