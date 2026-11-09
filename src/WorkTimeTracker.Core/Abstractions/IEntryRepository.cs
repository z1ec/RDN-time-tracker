using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Abstractions;

/// <summary>
/// Хранилище записей. Все выборки возвращают только неудалённые записи (<c>deleted_at IS NULL</c>).
/// Каждый метод изменения выполняется в отдельной транзакции (F4.1).
/// </summary>
public interface IEntryRepository
{
    /// <summary>Добавляет записи атомарно: либо все, либо ни одной (F1.2).</summary>
    Task<IReadOnlyList<WorkEntry>> AddAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default);

    /// <summary>Изменяет запись; возвращает <c>false</c>, если её нет (или она удалена).</summary>
    Task<bool> UpdateAsync(WorkEntry entry, CancellationToken ct = default);

    Task<WorkEntry?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>Записи с датой в диапазоне [from; to] включительно, по возрастанию даты и начала.</summary>
    Task<IReadOnlyList<WorkEntry>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Все записи по возрастанию даты и начала.</summary>
    Task<IReadOnlyList<WorkEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Дата самой ранней записи или <c>null</c>, если записей нет.</summary>
    Task<DateOnly?> GetFirstDateAsync(CancellationToken ct = default);

    /// <summary>Поиск по комментарию без учёта регистра, включая кириллицу (F6.4).</summary>
    Task<IReadOnlyList<WorkEntry>> SearchAsync(string text, CancellationToken ct = default);

    /// <summary>Мягкое удаление (F5.3): проставляет <c>deleted_at</c>. Возвращает число удалённых.</summary>
    Task<int> SoftDeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Отмена мягкого удаления. Возвращает число восстановленных.</summary>
    Task<int> RestoreAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Физически удаляет записи, мягко удалённые раньше <paramref name="olderThanUtc"/>.</summary>
    Task<int> PurgeDeletedAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default);
}
