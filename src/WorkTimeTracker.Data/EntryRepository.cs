using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Data;

/// <summary>
/// Хранилище записей в SQLite (Dapper). Каждое изменение — в отдельной транзакции (F4.1).
/// Все выборки — только по неудалённым записям (<c>deleted_at IS NULL</c>).
/// </summary>
public sealed class EntryRepository(SqliteConnectionFactory factory, TimeProvider time) : IEntryRepository
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string Columns = "id AS Id, work_date AS WorkDate, start_min AS StartMin, end_min AS EndMin, comment AS Comment";

    public Task<IReadOnlyList<WorkEntry>> AddAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default) =>
        factory.RunAsync<IReadOnlyList<WorkEntry>>(
            c =>
            {
                using var tx = c.BeginTransaction();
                var result = InsertEntries(c, tx, drafts, Now());
                tx.Commit();
                return result;
            },
            ct);

    /// <summary>Изменяет запись; связанная строка блокнота получает те же значения в той же транзакции (F9.6).</summary>
    public Task<bool> UpdateAsync(WorkEntry entry, CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                using var tx = c.BeginTransaction();
                var updated = UpdateEntry(c, tx, entry, Now());
                tx.Commit();
                return updated;
            },
            ct);

    public Task<WorkEntry?> GetAsync(long id, CancellationToken ct = default) =>
        factory.RunAsync(
            c => c.QuerySingleOrDefault<Row>($"SELECT {Columns} FROM entries WHERE id = @id AND deleted_at IS NULL", new { id })?.ToEntry(),
            ct);

    public Task<IReadOnlyList<WorkEntry>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Query(
            $"SELECT {Columns} FROM entries WHERE deleted_at IS NULL AND work_date BETWEEN @from AND @to ORDER BY work_date, start_min",
            new { from = FormatDate(from), to = FormatDate(to) },
            ct);

    public Task<IReadOnlyList<WorkEntry>> GetAllAsync(CancellationToken ct = default) =>
        Query($"SELECT {Columns} FROM entries WHERE deleted_at IS NULL ORDER BY work_date, start_min", null, ct);

    public Task<DateOnly?> GetFirstDateAsync(CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                var s = c.ExecuteScalar<string?>("SELECT MIN(work_date) FROM entries WHERE deleted_at IS NULL");
                return s is null ? (DateOnly?)null : ParseDate(s);
            },
            ct);

    /// <summary>
    /// Поиск без учёта регистра (F6.4). Встроенные LOWER/LIKE в SQLite понимают регистр только у ASCII,
    /// поэтому фильтрация идёт в C# с культурой ru-RU: «Созвон» находит «созвон».
    /// </summary>
    public async Task<IReadOnlyList<WorkEntry>> SearchAsync(string text, CancellationToken ct = default)
    {
        var needle = text.Trim();
        var all = await Query(
            $"SELECT {Columns} FROM entries WHERE deleted_at IS NULL AND comment <> '' ORDER BY work_date, start_min", null, ct).ConfigureAwait(false);
        if (needle.Length == 0)
        {
            return all;
        }

        var compare = RuFormat.Culture.CompareInfo;
        return [.. all.Where(e => compare.IndexOf(e.Comment, needle, CompareOptions.IgnoreCase) >= 0)];
    }

    public Task<int> SoftDeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        Execute("UPDATE entries SET deleted_at = @now, updated_at = @now WHERE id IN @ids AND deleted_at IS NULL", ids, ct);

    public Task<int> RestoreAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        Execute("UPDATE entries SET deleted_at = NULL, updated_at = @now WHERE id IN @ids AND deleted_at IS NOT NULL", ids, ct);

    public Task<int> PurgeDeletedAsync(DateTimeOffset olderThanUtc, CancellationToken ct = default) =>
        factory.RunAsync(
            c =>
            {
                using var tx = c.BeginTransaction();
                var n = c.Execute(
                    "DELETE FROM entries WHERE deleted_at IS NOT NULL AND deleted_at < @threshold",
                    new { threshold = FormatTimestamp(olderThanUtc) },
                    tx);
                tx.Commit();
                return n;
            },
            ct);

    /// <summary>Вставка записей внутри чужой транзакции — общая для журнала и блокнота (F8.4, F9.2).</summary>
    internal static List<WorkEntry> InsertEntries(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<EntryDraft> drafts, string now)
    {
        var result = new List<WorkEntry>(drafts.Count);
        foreach (var d in drafts)
        {
            var id = c.ExecuteScalar<long>(
                """
                INSERT INTO entries (work_date, start_min, end_min, comment, created_at, updated_at)
                VALUES (@date, @start, @end, @comment, @now, @now);
                SELECT last_insert_rowid();
                """,
                new { date = FormatDate(d.Date), start = d.Range.StartMinute, end = d.Range.EndMinute, comment = d.Comment, now },
                tx);
            result.Add(d.ToEntry(id));
        }

        return result;
    }

    /// <summary>
    /// Изменение записи внутри чужой транзакции — общее для «Журнала» и правки строки блокнота (F9.5, F9.6).
    /// Правка записи всегда переносится в связанную строку блокнота: последняя правка пользователя — в «Журнале».
    /// </summary>
    internal static bool UpdateEntry(SqliteConnection c, SqliteTransaction tx, WorkEntry entry, string now)
    {
        var args = new { id = entry.Id, date = FormatDate(entry.Date), start = entry.StartMinute, end = entry.EndMinute, comment = entry.Comment, now };
        var n = c.Execute(
            """
            UPDATE entries SET work_date = @date, start_min = @start, end_min = @end, comment = @comment, updated_at = @now
            WHERE id = @id AND deleted_at IS NULL
            """,
            args,
            tx);
        if (n == 1)
        {
            c.Execute(
                "UPDATE notepad_lines SET work_date = @date, start_min = @start, end_min = @end, comment = @comment, updated_at = @now WHERE entry_id = @id",
                args,
                tx);
        }

        return n == 1;
    }

    internal static string FormatDate(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    internal static DateOnly ParseDate(string s) => DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Фиксированный формат UTC-времени: строки сравниваются лексикографически.</summary>
    internal static string FormatTimestamp(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset ParseTimestamp(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private string Now() => FormatTimestamp(time.GetUtcNow());

    private Task<IReadOnlyList<WorkEntry>> Query(string sql, object? args, CancellationToken ct) =>
        factory.RunAsync<IReadOnlyList<WorkEntry>>(c => [.. c.Query<Row>(sql, args).Select(r => r.ToEntry())], ct);

    private Task<int> Execute(string sql, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Task.FromResult(0);
        }

        return factory.RunAsync(
            c =>
            {
                using var tx = c.BeginTransaction();
                var n = c.Execute(sql, new { ids, now = Now() }, tx);
                tx.Commit();
                return n;
            },
            ct);
    }

    private sealed class Row
    {
        public long Id { get; init; }

        public string WorkDate { get; init; } = "";

        public long StartMin { get; init; }

        public long EndMin { get; init; }

        public string Comment { get; init; } = "";

        public WorkEntry ToEntry() => new(Id, ParseDate(WorkDate), (int)StartMin, (int)EndMin, Comment);
    }
}
