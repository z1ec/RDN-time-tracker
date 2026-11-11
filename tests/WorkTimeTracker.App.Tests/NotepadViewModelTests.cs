using Avalonia.Headless.XUnit;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Data;
using static WorkTimeTracker.App.Tests.JournalViewModelTests;

namespace WorkTimeTracker.App.Tests;

/// <summary>
/// Вкладка «Блокнот» (F8.1–F8.20): сценарии приёмки S1–S12 на настоящих ViewModel и БД.
/// «Сегодня» — вторник 10.11.2026, 14:37.
/// </summary>
public class NotepadViewModelTests
{
    private static async Task AddAsync(NotepadViewModel vm, string text)
    {
        vm.Input.QuickInput = text;
        Assert.True(vm.Input.AddCommand.CanExecute(null), vm.Input.PreviewText);
        await vm.Input.AddCommand.ExecuteAsync(null);
    }

    /// <summary>Отпечаток статистики за месяц: все показатели и подписи. Меняется при любом изменении записей.</summary>
    private static async Task<string> StatsAsync(TestHost host)
    {
        host.Statistics.SelectedKindIndex = 2;
        await host.Statistics.LoadAsync();
        await WaitAsync(() => host.Statistics.Period.Kind == Core.Model.PeriodKind.Month);
        await host.Statistics.LoadAsync();
        return string.Join(" | ", host.Statistics.Kpis.Select(k => $"{k.Title}={k.Value} ({k.Caption})"));
    }

    private static List<EntryRow> JournalEntries(TestHost host) => [.. host.Journal.Rows.OfType<EntryRow>()];

    private static async Task<string> EntriesDumpAsync(TestHost host)
    {
        var rows = await host.Services.GetRequiredService<SqliteConnectionFactory>().RunAsync(c => c.Query<string>(
            "SELECT id || '|' || work_date || '|' || start_min || '|' || end_min || '|' || comment || '|' || updated_at || '|' || coalesce(deleted_at, '') FROM entries ORDER BY id").ToList());
        return string.Join("\n", rows);
    }

    // ---------- S1 ----------

    [AvaloniaFact]
    public async Task S1_DayInPieces_SumJournalAndStatistics()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        Assert.True(vm.IsEmpty);
        Assert.Equal("В блокноте: 0 ч 00 мин · 0 записей", vm.SummaryText);

        await AddAsync(vm, "0900 1230 задача А");
        await AddAsync(vm, "1330 1800 задача Б");

        Assert.Equal("В блокноте: 8 ч 00 мин · 2 записи · ведётся с 10 нояб., 14:37", vm.SummaryText);
        Assert.Equal(["задача А", "задача Б"], vm.Rows.Select(r => r.Comment));
        Assert.All(vm.Rows, r => Assert.Equal("10 нояб.", r.DateText)); // F8.8: в каждой строке видна дата
        Assert.Equal("", vm.Input.QuickInput);
        Assert.Equal("Добавлено: 1 запись", vm.StatusText);
        Assert.False(vm.IsEmpty);

        await WaitAsync(() => JournalEntries(host).Count == 2); // «Журнал» узнал об изменении сам
        Assert.Equal(["задача А", "задача Б"], JournalEntries(host).Select(r => r.Comment));
        Assert.Equal("Сегодня: 8 ч 00 мин · Неделя: 8 ч 00 мин из 40 ч", host.Journal.SummaryText);

        host.Statistics.SelectedKindIndex = 0; // день
        await WaitAsync(() => host.Statistics.Period.Kind == Core.Model.PeriodKind.Day);
        await host.Statistics.LoadAsync();
        Assert.Equal(("Итого", "8:00"), (host.Statistics.Kpis[0].Title, host.Statistics.Kpis[0].Value));
    }

    [AvaloniaFact]
    public async Task NightShift_CountsWhole_OrderByDateAndStart()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "1000 1100 сегодня");
        await AddAsync(vm, "08.11 2200 0200 ночь\n09.11 0900 1000 вчера");

        Assert.Equal(["8 нояб.", "9 нояб.", "10 нояб."], vm.Rows.Select(r => r.DateText));
        Assert.True(vm.Rows[0].EndsNextDay);
        Assert.StartsWith("В блокноте: 6 ч 00 мин · 3 записи", vm.SummaryText, StringComparison.Ordinal);
        Assert.Equal("Добавлено: 2 записи", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Form_AddsToNotepadAndJournal()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        vm.Input.FormStart = new TimeSpan(9, 0, 0);
        vm.Input.FormEnd = new TimeSpan(10, 30, 0);
        vm.Input.FormComment = "из формы";
        Assert.True(vm.Input.AddFromFormCommand.CanExecute(null));

        await vm.Input.AddFromFormCommand.ExecuteAsync(null);

        Assert.Equal("из формы", Assert.Single(vm.Rows).Comment);
        Assert.Single(await host.Repository.GetAllAsync());
        Assert.Null(vm.Input.FormStart);
    }

    // ---------- S2 ----------

    [AvaloniaFact]
    public async Task S2_MultiDayOrder_Clear_ArchiveCard_StatisticsUnchanged()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        vm.Title = "Заказ: сайт кафе";
        await vm.TitleSaveTask;
        await AddAsync(vm, "08.11 0900 1700 макет\n09.11 0900 1645 вёрстка\n0900 1630 правки");
        Assert.StartsWith("В блокноте: 23 ч 15 мин · 3 записи", vm.SummaryText, StringComparison.Ordinal);
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);
        host.Time.Advance(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(3));

        Assert.True(vm.ClearAllCommand.CanExecute(null));
        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Rows);
        Assert.Equal("", vm.Title);
        Assert.Equal("В блокноте: 0 ч 00 мин · 0 записей", vm.SummaryText);
        Assert.False(vm.ClearAllCommand.CanExecute(null));
        Assert.Equal("Блокнот перенесён в архив", vm.Undo.Text);
        Assert.True(vm.Undo.IsVisible);
        Assert.Equal("Архив (1)", vm.ArchiveTabText);
        var card = Assert.Single(vm.ArchiveSheets);
        Assert.Equal("Заказ: сайт кафе", card.Title);
        Assert.Equal("8–10 нояб.", card.DateRangeText);
        Assert.Equal("23 ч 15 мин · 3 записи", card.TotalsText);
        Assert.Equal("очищен 10 нояб., 18:40", card.ClearedText);
        Assert.Equal("8–10 нояб. · 23 ч 15 мин · 3 записи · очищен 10 нояб., 18:40", card.SubtitleText);
        Assert.False(card.AllEntriesDeleted);
        Assert.Equal(["макет", "вёрстка", "правки"], card.Entries.Select(e => e.Comment));

        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host)); // записи не тронуты ни одним байтом
        Assert.Equal(3, JournalEntries(host).Count);
    }

    // ---------- S3 ----------

    [AvaloniaFact]
    public async Task S3_Restart_KeepsSheetTitleEntriesAndArchive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wtt-app-tests", Guid.NewGuid().ToString("N"));
        using var first = await TestHost.StartAsync(dataDir: dir);
        await AddAsync(first.Notepad, "09.11 0900 1000 старый лист");
        await first.Notepad.ClearAllCommand.ExecuteAsync(null);
        first.Notepad.Title = "Заказ: сайт кафе";
        await first.Notepad.TitleSaveTask;
        await AddAsync(first.Notepad, "0900 1200 задача А\n1300 1400 задача Б");
        await first.Notepad.RemoveEntryCommand.ExecuteAsync(first.Notepad.Rows[1]);
        var summary = first.Notepad.SummaryText;
        var firstVm = first.Notepad;
        first.Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var second = await TestHost.StartAsync(dataDir: dir, keepExisting: true);
        var vm = second.Notepad;

        Assert.NotSame(firstVm, vm);
        Assert.Equal("Заказ: сайт кафе", vm.Title);
        Assert.Equal("задача А", Assert.Single(vm.Rows).Comment);
        Assert.Equal(summary, vm.SummaryText);
        Assert.Equal("В блокноте: 3 ч 00 мин · 1 запись · ведётся с 10 нояб., 14:37", vm.SummaryText);
        var card = Assert.Single(vm.ArchiveSheets);
        Assert.Equal("Лист 9 нояб.", card.Title);
        Assert.Equal("старый лист", Assert.Single(card.Entries).Comment);
        Assert.Equal(3, JournalEntries(second).Count);
        Assert.False(vm.Undo.UndoCommand.CanExecute(null)); // отмена не переживает перезапуск
    }

    // ---------- S4 ----------

    [AvaloniaFact]
    public async Task S4_EditInNotepad_ChangesEntryEverywhere()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1900 работа");
        await WaitAsync(() => JournalEntries(host).Count == 1);
        host.Interaction.OnEdit = async editor =>
        {
            Assert.Equal(new TimeSpan(19, 0, 0), editor.End);
            editor.End = new TimeSpan(18, 0, 0);
            return await editor.SaveAsync();
        };

        await vm.EditEntryCommand.ExecuteAsync(vm.Rows[0]);

        Assert.Equal("09:00 – 18:00", vm.Rows[0].TimeText);
        Assert.StartsWith("В блокноте: 9 ч 00 мин · 1 запись", vm.SummaryText, StringComparison.Ordinal);
        Assert.Equal("Строка изменена · в статистике", vm.StatusText);
        Assert.True(vm.Rows[0].IsInStatistics);
        await WaitAsync(() => JournalEntries(host).Single().TimeText == "09:00 – 18:00");
        Assert.StartsWith("Сегодня: 9 ч 00 мин", host.Journal.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Итого=9:00", await StatsAsync(host), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Edit_Cancelled_Or_Null_ChangesNothing()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000");
        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        await vm.Undo.UndoCommand.ExecuteAsync(null);
        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        await vm.Undo.UndoCommand.ExecuteAsync(null);

        await vm.EditEntryCommand.ExecuteAsync(vm.Rows[0]); // диалог закрыт без сохранения
        await vm.EditEntryCommand.ExecuteAsync(null);
        await vm.RemoveEntryCommand.ExecuteAsync(null);
        await vm.RemoveSelectedCommand.ExecuteAsync(null);
        await vm.DeleteSheetCommand.ExecuteAsync(null);

        Assert.Single(vm.Rows);
        Assert.False(vm.Undo.IsVisible);
    }

    // ---------- S5 ----------

    [AvaloniaFact]
    public async Task S5_Remove_KeepsEntryInJournalAndStatistics_UndoReturns()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1200 задача А\n1300 1800 задача Б");
        await WaitAsync(() => JournalEntries(host).Count == 2);
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);

        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);

        Assert.Equal("задача Б", Assert.Single(vm.Rows).Comment);
        Assert.StartsWith("В блокноте: 5 ч 00 мин · 1 запись", vm.SummaryText, StringComparison.Ordinal);
        Assert.True(vm.Undo.IsVisible);
        Assert.Equal("Убрано из блокнота: 1 запись", vm.Undo.Text);
        Assert.Equal(2, JournalEntries(host).Count);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));

        await vm.Undo.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["задача А", "задача Б"], vm.Rows.Select(r => r.Comment));
        Assert.StartsWith("В блокноте: 8 ч 00 мин · 2 записи", vm.SummaryText, StringComparison.Ordinal);
        Assert.Equal("Возвращено в блокнот: 1 запись", vm.StatusText);
        Assert.False(vm.Undo.IsVisible);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));
    }

    [AvaloniaFact]
    public async Task Remove_MultipleSelected_ToastHidesAfter8Seconds_UndoStillWorks()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000 a\n1100 1200 b\n1300 1400 c");
        vm.SelectedRows.Add(vm.Rows[0]);
        vm.SelectedRows.Add(vm.Rows[2]);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.Equal("b", Assert.Single(vm.Rows).Comment);
        Assert.Equal("Убрано из блокнота: 2 записи", vm.Undo.Text);
        host.Time.Advance(UndoToastViewModel.Duration);
        await WaitAsync(() => !vm.Undo.IsVisible);
        Assert.True(vm.Undo.UndoCommand.CanExecute(null)); // Ctrl+Z работает и после скрытия уведомления

        await vm.Undo.UndoCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(3, (await host.Repository.GetAllAsync()).Count);
    }

    [AvaloniaFact]
    public async Task Undo_UnavailableAfterNextNotepadChange()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000 a\n1100 1200 b");
        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        Assert.True(vm.Undo.UndoCommand.CanExecute(null));

        await AddAsync(vm, "1300 1400 c");
        Assert.False(vm.Undo.UndoCommand.CanExecute(null));
        Assert.False(vm.Undo.IsVisible);

        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        vm.Title = "новое название";
        await vm.TitleSaveTask;
        Assert.False(vm.Undo.UndoCommand.CanExecute(null));
    }

    // ---------- S6 ----------

    [AvaloniaFact]
    public async Task S6_DeleteInJournal_RemovesFromNotepadAndArchive_UndoRestores()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "09.11 0900 1100 архивная А\n09.11 1200 1300 архивная Б");
        await vm.ClearAllCommand.ExecuteAsync(null);
        await AddAsync(vm, "0900 1200 текущая А\n1300 1800 текущая Б");
        await WaitAsync(() => JournalEntries(host).Count == 4);
        var journal = host.Journal;

        foreach (var row in JournalEntries(host).Where(r => r.Comment.EndsWith('А')))
        {
            journal.SelectedRows.Add(row);
        }

        await journal.DeleteSelectedCommand.ExecuteAsync(null);

        await WaitAsync(() => vm.Rows.Count == 1);
        Assert.Equal("текущая Б", vm.Rows[0].Comment);
        Assert.StartsWith("В блокноте: 5 ч 00 мин · 1 запись", vm.SummaryText, StringComparison.Ordinal);
        var card = Assert.Single(vm.ArchiveSheets);
        Assert.Equal("архивная Б", Assert.Single(card.Entries).Comment);
        Assert.Equal("1 ч 00 мин · 1 запись", card.TotalsText);

        await journal.Undo.UndoCommand.ExecuteAsync(null);

        await WaitAsync(() => vm.Rows.Count == 2);
        Assert.Equal(["текущая А", "текущая Б"], vm.Rows.Select(r => r.Comment));
        Assert.StartsWith("В блокноте: 8 ч 00 мин · 2 записи", vm.SummaryText, StringComparison.Ordinal);
        Assert.Equal(["архивная А", "архивная Б"], Assert.Single(vm.ArchiveSheets).Entries.Select(e => e.Comment));
        Assert.Equal("3 ч 00 мин · 2 записи", vm.ArchiveSheets[0].TotalsText);
    }

    [AvaloniaFact]
    public async Task Archive_IsLive_EditedAndAllDeleted()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1900 работа");
        await vm.ClearAllCommand.ExecuteAsync(null);
        await WaitAsync(() => JournalEntries(host).Count == 1);
        vm.ArchiveSheets[0].ToggleExpandedCommand.Execute(null);

        host.Interaction.OnEdit = async editor =>
        {
            editor.End = new TimeSpan(18, 0, 0);
            editor.Comment = "исправлено";
            return await editor.SaveAsync();
        };
        await host.Journal.EditEntryCommand.ExecuteAsync(JournalEntries(host).Single());

        await WaitAsync(() => vm.ArchiveSheets.Count == 1 && vm.ArchiveSheets[0].Entries.Count == 1 && vm.ArchiveSheets[0].Entries[0].Comment == "исправлено");
        Assert.Equal("9 ч 00 мин · 1 запись", vm.ArchiveSheets[0].TotalsText); // правка в «Журнале» видна в архиве
        Assert.True(vm.ArchiveSheets[0].IsExpanded); // раскрытая карточка остаётся раскрытой после обновления
        Assert.False(vm.ArchiveSheets[0].ShowDeletedNote);

        await host.Journal.DeleteEntryCommand.ExecuteAsync(JournalEntries(host).Single());

        await WaitAsync(() => vm.ArchiveSheets.Count == 1 && vm.ArchiveSheets[0].AllEntriesDeleted);
        var card = vm.ArchiveSheets[0]; // лист без записей остаётся в архиве с пометкой
        Assert.Equal("записи удалены", card.DateRangeText);
        Assert.Equal("Лист от 10 нояб.", card.Title);
        Assert.Equal("0 ч 00 мин · 0 записей · очищен 10 нояб., 14:37", card.SubtitleText);
        Assert.True(card.ShowDeletedNote);
        Assert.Contains("записи удалены", card.AccessibleName, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ChangeInNotepad_InvalidatesJournalUndo()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0900 1200 из журнала";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        await host.Journal.DeleteEntryCommand.ExecuteAsync(JournalEntries(host).Single());
        Assert.True(host.Journal.Undo.UndoCommand.CanExecute(null));

        await AddAsync(host.Notepad, "1000 1100 на освободившееся место");

        // Иначе Ctrl+Z в «Журнале» вернул бы запись 09:00–12:00 поверх новой — пересечение.
        Assert.False(host.Journal.Undo.UndoCommand.CanExecute(null));
        Assert.Single(await host.Repository.GetAllAsync());
    }

    // ---------- S7 (переписан в v1.2.0: F9.2) ----------

    [AvaloniaFact]
    public async Task S7_OverlapWithJournalEntry_LineOnlyInNotepad()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0900 1200 из журнала";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        var vm = host.Notepad;
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);

        await AddAsync(vm, "1000 1100");

        Assert.False(vm.Input.PreviewIsError); // пересечение в блокноте — не ошибка
        Assert.Equal("", vm.Input.QuickInput);
        Assert.Equal("Добавлено: 1 запись · не в статистике: 1 (пересечение с журналом)", vm.StatusText);
        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsNotInStatistics);
        Assert.Equal("не в статистике", row.StatusLabel);
        Assert.Equal("Пересекается с записью 10.11 09:00–12:00 «из журнала» — в статистику не записано", row.StatusHint);
        Assert.Equal("В блокноте: 1 ч 00 мин · в статистике: 0 ч 00 мин · 1 запись · ведётся с 10 нояб., 14:37", vm.SummaryText);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host)); // в базу ничего не записано
        Assert.Single(JournalEntries(host));
    }

    // ---------- S8 ----------

    [AvaloniaFact]
    public async Task S8_AccidentalClear_UndoRestoresSheetWithTitle()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        vm.Title = "Заказ: сайт кафе";
        await vm.TitleSaveTask;
        await AddAsync(vm, "0900 1200 задача А\n1300 1800 задача Б");
        var summary = vm.SummaryText;
        var stats = await StatsAsync(host);

        await vm.ClearAllCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
        Assert.Single(vm.ArchiveSheets);
        Assert.True(vm.Undo.UndoCommand.CanExecute(null));

        await vm.Undo.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Заказ: сайт кафе", vm.Title);
        Assert.Equal(["задача А", "задача Б"], vm.Rows.Select(r => r.Comment));
        Assert.Equal(summary, vm.SummaryText);
        Assert.Empty(vm.ArchiveSheets);
        Assert.True(vm.IsArchiveEmpty);
        Assert.Equal("Архив (0)", vm.ArchiveTabText);
        Assert.Equal("Блокнот восстановлен", vm.StatusText);
        Assert.True(vm.ClearAllCommand.CanExecute(null));
        Assert.Equal(stats, await StatsAsync(host));
    }

    [AvaloniaFact]
    public async Task Clear_UndoUnavailable_AfterNextChange()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000 первый лист");
        await vm.ClearAllCommand.ExecuteAsync(null);

        await AddAsync(vm, "1100 1200 второй лист");

        Assert.False(vm.Undo.UndoCommand.CanExecute(null));
        Assert.Equal("второй лист", Assert.Single(vm.Rows).Comment);
        Assert.Single(vm.ArchiveSheets);
    }

    [AvaloniaFact]
    public async Task Clear_Disabled_WhenEmpty()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        Assert.False(vm.ClearAllCommand.CanExecute(null));
        Assert.False(vm.CanClear);

        vm.Title = "только название";
        await vm.TitleSaveTask;
        Assert.False(vm.ClearAllCommand.CanExecute(null)); // лист есть, записей нет
        Assert.Equal("В блокноте: 0 ч 00 мин · 0 записей · ведётся с 10 нояб., 14:37", vm.SummaryText);

        await AddAsync(vm, "0900 1000");
        Assert.True(vm.ClearAllCommand.CanExecute(null));

        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        Assert.False(vm.ClearAllCommand.CanExecute(null)); // все записи убраны — снова пусто
        Assert.Empty(vm.ArchiveSheets);
    }

    // ---------- S9 ----------

    [AvaloniaFact]
    public async Task S9_DeleteFromArchive_KeepsEntries_UndoReturnsSheet()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1200 задача А\n1300 1800 задача Б");
        await vm.ClearAllCommand.ExecuteAsync(null);
        await WaitAsync(() => JournalEntries(host).Count == 2);
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);

        await vm.DeleteSheetCommand.ExecuteAsync(vm.ArchiveSheets[0]);

        Assert.Empty(vm.ArchiveSheets);
        Assert.True(vm.IsArchiveEmpty);
        Assert.Equal("Архив (0)", vm.ArchiveTabText);
        Assert.True(vm.Undo.IsVisible);
        Assert.Equal("Лист удалён из архива · записи остались в журнале", vm.Undo.Text);
        Assert.Equal(2, JournalEntries(host).Count);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));

        await vm.Undo.UndoCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.ArchiveSheets);
        Assert.Equal(2, card.Entries.Count);
        Assert.Equal("Лист возвращён в архив", vm.StatusText);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));
    }

    [AvaloniaFact]
    public async Task Archive_NewestFirst_Tabs_EmptyStates()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        Assert.True(vm.IsCurrentVisible);
        Assert.False(vm.IsArchiveVisible);
        Assert.True(vm.IsArchiveEmpty);
        Assert.Equal("Блокнот пуст — запишите время, например 0900 1800 задача. Записи без пересечений попадут и в журнал, и в статистику.", NotepadViewModel.EmptyCurrentText);
        Assert.Equal("Здесь появятся очищенные блокноты", NotepadViewModel.EmptyArchiveText);
        Assert.Equal(100, vm.MaxTitleLength);

        await AddAsync(vm, "03.11 0900 1000\n06.11 0900 1000");
        await vm.ClearAllCommand.ExecuteAsync(null);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        vm.Title = "Второй";
        await vm.TitleSaveTask;
        await AddAsync(vm, "0900 1000");
        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.Equal(["Второй", "Лист 3–6 нояб."], vm.ArchiveSheets.Select(s => s.Title));
        Assert.Equal("Архив (2)", vm.ArchiveTabText);
        Assert.False(vm.IsArchiveEmpty);

        vm.ShowArchiveCommand.Execute(null);
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.True(vm.IsArchiveVisible);
        Assert.False(vm.IsCurrentVisible);
        vm.ShowCurrentCommand.Execute(null);
        Assert.True(vm.IsCurrentVisible);

        var card = vm.ArchiveSheets[1];
        Assert.False(card.IsExpanded);
        card.ToggleExpandedCommand.Execute(null);
        Assert.True(card.IsExpanded);
        Assert.Equal("Лист 3–6 нояб., 3–6 нояб., 2 ч 00 мин · 2 записи, очищен 10 нояб., 14:37", card.AccessibleName);
    }

    // ---------- S10 ----------

    [AvaloniaFact]
    public async Task S10_EntryAddedInJournal_DoesNotAppearInNotepad()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000 из блокнота");
        var summary = vm.SummaryText;

        host.Journal.Input.QuickInput = "1100 1200 из журнала";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        await UiTestHelpers.SettleAsync();
        await vm.LoadAsync();

        Assert.Equal("из блокнота", Assert.Single(vm.Rows).Comment);
        Assert.Equal(summary, vm.SummaryText);
        Assert.Equal(2, JournalEntries(host).Count);
    }

    // ---------- S11 ----------

    [AvaloniaFact]
    public async Task S11_MultilinePaste_WithInvalidLine_AddsNothing()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;

        vm.Input.QuickInput = "0900 1000 первая\n2500 1100\n1200";
        Assert.Equal("✗ Строка 2: Часы должны быть от 0 до 23 — «2500»" + Environment.NewLine + "✗ Строка 3: Не указано время окончания", vm.Input.PreviewText);
        Assert.False(vm.Input.AddCommand.CanExecute(null));
        var result = await host.NotepadService.AddFromTextAsync(vm.Input.QuickInput, host.Today); // даже в обход кнопки
        Assert.Equal(2, result.Errors.Count);

        // Пересечение строк друг с другом в блокноте — не ошибка (F9.2, сценарий S23).
        await vm.LoadAsync();
        Assert.True(vm.IsEmpty);
        Assert.Empty(await host.Repository.GetAllAsync());
        Assert.Null(await host.NotepadService.GetCurrentAsync());
    }

    // ---------- S12 ----------

    [AvaloniaFact]
    public async Task S12_StartupAfter30Days_PurgesNotepadGarbage_NotEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wtt-app-tests", Guid.NewGuid().ToString("N"));
        using var first = await TestHost.StartAsync(dataDir: dir);
        var vm = first.Notepad;
        await AddAsync(vm, "08.11 0900 1000 удалённый лист");
        await vm.ClearAllCommand.ExecuteAsync(null);
        await vm.DeleteSheetCommand.ExecuteAsync(vm.ArchiveSheets[0]);
        await AddAsync(vm, "09.11 0900 1000 архив остаётся");
        await vm.ClearAllCommand.ExecuteAsync(null);
        await AddAsync(vm, "0900 1000 убрана\n1100 1200 осталась");
        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        var dump = await EntriesDumpAsync(first);
        first.Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Через 29 дней ничего не удаляется, через 31 — мусор удаляется физически.
        var start = new DateTimeOffset(2026, 11, 10, 14, 37, 0, TimeSpan.Zero);
        using (var early = await TestHost.StartAsync(start.AddDays(29), dataDir: dir, keepExisting: true))
        {
            early.KeepDataOnDispose = true;
            Assert.Equal((4, 3), await CountsAsync(early));
        }

        using var late = await TestHost.StartAsync(start.AddDays(31), dataDir: dir, keepExisting: true);

        Assert.Equal((2, 2), await CountsAsync(late)); // убранная строка и строка удалённого листа; удалённый лист
        Assert.Equal(dump, await EntriesDumpAsync(late)); // записи не тронуты
        Assert.Equal("осталась", Assert.Single(late.Notepad.Rows).Comment);
        Assert.Equal("архив остаётся", Assert.Single(Assert.Single(late.Notepad.ArchiveSheets).Entries).Comment);
        Assert.Equal(4, (await late.Repository.GetAllAsync()).Count);

        static Task<(int Items, int Sheets)> CountsAsync(TestHost host) =>
            host.Services.GetRequiredService<SqliteConnectionFactory>().RunAsync(c =>
                (c.ExecuteScalar<int>("SELECT count(*) FROM notepad_lines"), c.ExecuteScalar<int>("SELECT count(*) FROM notepad_sheets")));
    }

    // ---------- Название (F8.7) ----------

    [AvaloniaFact]
    public async Task Title_SavedTrimmed_LazySheet_EmptyTitleDoesNotCreateSheet()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;

        vm.Title = "   ";
        await vm.TitleSaveTask;
        Assert.Null(await host.NotepadService.GetCurrentAsync());
        Assert.Equal("", vm.Title);

        vm.Title = "  Заказ: сайт кафе  ";
        await vm.TitleSaveTask;
        Assert.Equal("Заказ: сайт кафе", vm.Title);
        Assert.Equal("Заказ: сайт кафе", (await host.NotepadService.GetCurrentAsync())!.Sheet.Title);

        vm.Title = new string('я', 150);
        await vm.TitleSaveTask;
        Assert.Equal(100, vm.Title.Length);
    }

    [AvaloniaFact]
    public async Task Title_ThenImmediateClear_TitleStaysWithArchivedSheet()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000");

        vm.Title = "Заказ"; // как при уходе фокуса из поля по нажатию «Очистить всё»: сохранение не дожидаемся
        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.Equal("Заказ", Assert.Single(vm.ArchiveSheets).Title);
        Assert.Null(await host.NotepadService.GetCurrentAsync()); // название не досталось новому листу
        Assert.True(vm.Undo.UndoCommand.CanExecute(null));
        await vm.Undo.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Заказ", vm.Title);
        Assert.Single(vm.Rows);
    }

    [AvaloniaFact]
    public async Task Title_ThenImmediateAdd_OneSheetWithTitleAndEntry()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;

        vm.Title = "Первое";
        vm.Title = "Заказ";
        await AddAsync(vm, "0900 1000");

        var current = (await host.NotepadService.GetCurrentAsync())!;
        Assert.Equal(("Заказ", 1), (current.Sheet.Title, current.Lines.Count));
        Assert.Equal("Заказ", vm.Title);
    }

    // ---------- S13: исходный баг ----------

    [AvaloniaFact]
    public async Task S13_AddRemoveAddAgain_SecondLineAccepted()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "1100 1300");
        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);

        vm.Input.QuickInput = "1100 1300";
        await vm.Input.AddCommand.ExecuteAsync(null);

        Assert.False(vm.Input.PreviewIsError, vm.Input.PreviewText);
        Assert.Single(vm.Rows);
    }

    // ---------- Отмена по активной вкладке (F8.20) ----------

    [AvaloniaFact]
    public async Task ActiveUndo_FollowsCurrentPage()
    {
        using var host = await TestHost.StartAsync();
        var main = host.Main;
        Assert.Same(host.Journal.Undo, main.ActiveUndo);
        main.NavigateCommand.Execute("2");
        Assert.Same(host.Notepad.Undo, main.ActiveUndo);
        main.NavigateCommand.Execute("3");
        Assert.Null(main.ActiveUndo);
        main.NavigateCommand.Execute("4");
        Assert.Null(main.ActiveUndo);
        Assert.NotSame(host.Journal.Undo, host.Notepad.Undo);
    }
}
