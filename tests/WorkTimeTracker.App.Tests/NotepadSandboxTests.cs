using Avalonia.Headless.XUnit;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Core.Model;
using static WorkTimeTracker.App.Tests.JournalViewModelTests;
using static WorkTimeTracker.App.Tests.NotepadViewModelTests;

namespace WorkTimeTracker.App.Tests;

/// <summary>
/// Блокнот-песочница (F9.1–F9.10): сценарии приёмки S13–S26 на настоящих ViewModel и БД.
/// «Сегодня» — вторник 10.11.2026, 14:37. «Статистика не изменилась» — отпечаток показателей месяца
/// и побайтный дамп <c>entries</c>, как в S1–S12.
/// </summary>
public class NotepadSandboxTests
{
    private static async Task JournalAddAsync(TestHost host, string text)
    {
        host.Journal.Input.QuickInput = text;
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        Assert.False(host.Journal.Input.PreviewIsError, host.Journal.Input.PreviewText);
    }

    /// <summary>Правка строки через тот же диалог, что в «Журнале»: меняем время и сохраняем.</summary>
    private static async Task EditAsync(TestHost host, NotepadLineRow row, TimeSpan start, TimeSpan end, string? comment = null)
    {
        host.Interaction.OnEdit = async editor =>
        {
            Assert.Equal("Изменить строку блокнота", editor.Title);
            Assert.Equal(NotepadViewModel.EditHint, editor.Hint);
            Assert.True(editor.HasHint);
            editor.Start = start;
            editor.End = end;
            if (comment is not null)
            {
                editor.Comment = comment;
            }

            Assert.True(await editor.SaveAsync(), editor.Error); // пересечение — не ошибка диалога (F9.5)
            return true;
        };
        await host.Notepad.EditEntryCommand.ExecuteAsync(row);
    }

    // ---------- S13: исходный баг ----------

    [AvaloniaFact]
    public async Task S13_AddRemoveAddAgain_SecondLineNotInStatistics()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "1100 1300");
        await vm.RemoveEntryCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Empty(vm.Rows);

        vm.Input.QuickInput = "1100 1300";
        Assert.True(vm.Input.AddCommand.CanExecute(null));
        await vm.Input.AddCommand.ExecuteAsync(null);

        Assert.False(vm.Input.PreviewIsError, vm.Input.PreviewText); // до v1.2.0: «✗ Пересекается с записью 10.11 11:00–13:00»
        Assert.Equal("", vm.Input.QuickInput);
        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsNotInStatistics);
        Assert.Equal("не в статистике", row.StatusLabel);
        Assert.Equal("Пересекается с записью 10.11 11:00–13:00 — в статистику не записано", row.StatusHint);
        Assert.Equal(0.55, row.ContentOpacity);
        Assert.StartsWith("В блокноте: 2 ч 00 мин", vm.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Итого=2:00", await StatsAsync(host), StringComparison.Ordinal); // без задвоения
        await WaitAsync(() => JournalEntries(host).Count == 1);
        Assert.Single(await host.Repository.GetAllAsync());
    }

    // ---------- S14–S16: суммы и статусы при вводе ----------

    [AvaloniaFact]
    public async Task S14_OverlapInsideNotepad_TwoSums()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1300");
        Assert.False(vm.HasSummaryHint);

        await AddAsync(vm, "1200 1500");

        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.Rows[0].IsInStatistics);
        Assert.Equal("", vm.Rows[0].StatusLabel);
        Assert.True(vm.Rows[1].IsNotInStatistics);
        Assert.Equal("В блокноте: 7 ч 00 мин · в статистике: 4 ч 00 мин · 2 записи · ведётся с 10 нояб., 14:37", vm.SummaryText);
        Assert.True(vm.HasSummaryHint);
        Assert.Equal("Добавлено: 1 запись · не в статистике: 1 (пересечение с журналом)", vm.StatusText);
        Assert.Contains("Итого=4:00", await StatsAsync(host), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task S15_Estimates_OnlyFirstInStatistics()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "1000 1200");
        await AddAsync(vm, "1000 1230");
        await AddAsync(vm, "1000 1300");

        Assert.Equal([true, false, false], vm.Rows.Select(r => r.IsInStatistics));
        Assert.Equal(["10:00 – 12:00", "10:00 – 12:30", "10:00 – 13:00"], vm.Rows.Select(r => r.TimeText));
        Assert.StartsWith("В блокноте: 7 ч 30 мин · в статистике: 2 ч 00 мин · 3 записи", vm.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Итого=2:00", await StatsAsync(host), StringComparison.Ordinal);
        Assert.Single(await host.Repository.GetAllAsync());
    }

    [AvaloniaFact]
    public async Task S16_EmptyDay_AllInStatistics_NoSecondFigure()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000 a");
        await AddAsync(vm, "1000 1200 b");
        await AddAsync(vm, "1300 1400 c");

        Assert.All(vm.Rows, r => Assert.True(r.IsInStatistics));
        Assert.All(vm.Rows, r => Assert.False(r.HasStatusLabel));
        Assert.Equal("В блокноте: 4 ч 00 мин · 3 записи · ведётся с 10 нояб., 14:37", vm.SummaryText);
        Assert.False(vm.HasSummaryHint);
        Assert.Equal(3, (await host.Repository.GetAllAsync()).Count);
    }

    // ---------- S17–S21: правка ----------

    [AvaloniaFact]
    public async Task S17_EditLinkedLine_NoOverlap_EntryAndStatisticsUpdated()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1900 работа");
        await WaitAsync(() => JournalEntries(host).Count == 1);

        await EditAsync(host, vm.Rows[0], new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0));

        Assert.Equal("09:00 – 18:00", vm.Rows[0].TimeText);
        Assert.True(vm.Rows[0].IsInStatistics);
        Assert.Equal("Строка изменена · в статистике", vm.StatusText);
        await WaitAsync(() => JournalEntries(host).Single().TimeText == "09:00 – 18:00");
        Assert.Contains("Итого=9:00", await StatsAsync(host), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task S18_EditLinkedLine_WithOverlap_DifferentInStatistics_DatabaseUnchanged()
    {
        using var host = await TestHost.StartAsync();
        await JournalAddAsync(host, "1800 2000 вечер");
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1700 работа");
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);
        var journalUndoBefore = host.Journal.Undo.UndoCommand.CanExecute(null);

        await EditAsync(host, vm.Rows[0], new TimeSpan(9, 0, 0), new TimeSpan(19, 0, 0));

        var row = Assert.Single(vm.Rows);
        Assert.Equal("09:00 – 19:00", row.TimeText);
        Assert.True(row.IsDifferentInStatistics);
        Assert.Equal("в статистике: 09:00–17:00", row.StatusLabel);
        Assert.Equal("Новое время пересекается с записью 10.11 18:00–20:00 «вечер» — в статистике осталось прежнее", row.StatusHint);
        Assert.Equal(1.0, row.ContentOpacity);
        Assert.StartsWith("Строка изменена только в блокноте · Новое время пересекается", vm.StatusText, StringComparison.Ordinal);
        Assert.StartsWith("В блокноте: 10 ч 00 мин · в статистике: 8 ч 00 мин · 1 запись", vm.SummaryText, StringComparison.Ordinal);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));
        Assert.Equal(journalUndoBefore, host.Journal.Undo.UndoCommand.CanExecute(null)); // записи не менялись — «Журналу» нечего сбрасывать
    }

    [AvaloniaFact]
    public async Task S19_EditFreeLine_NoOverlap_EntryCreated_StatisticsGrow()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1200");
        await AddAsync(vm, "1100 1300 прикидка");
        Assert.True(vm.Rows[1].IsNotInStatistics);

        await EditAsync(host, vm.Rows[1], new TimeSpan(12, 0, 0), new TimeSpan(14, 0, 0));

        Assert.True(vm.Rows[1].IsInStatistics);
        Assert.Equal("12:00 – 14:00", vm.Rows[1].TimeText);
        Assert.Equal("В блокноте: 5 ч 00 мин · 2 записи · ведётся с 10 нояб., 14:37", vm.SummaryText);
        await WaitAsync(() => JournalEntries(host).Count == 2); // «Журнал» узнал о новой записи
        Assert.Contains("прикидка", JournalEntries(host).Select(r => r.Comment));
        Assert.Contains("Итого=5:00", await StatsAsync(host), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task S20_EditFreeLine_StillOverlaps_StaysNotInStatistics()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1200");
        await AddAsync(vm, "1100 1300");
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);

        await EditAsync(host, vm.Rows[1], new TimeSpan(10, 0, 0), new TimeSpan(13, 0, 0), "передумал");

        Assert.Equal("10:00 – 13:00", vm.Rows[1].TimeText);
        Assert.Equal("передумал", vm.Rows[1].Comment);
        Assert.True(vm.Rows[1].IsNotInStatistics);
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));
    }

    [AvaloniaFact]
    public async Task S21_EditInJournal_OverridesDifferentLine()
    {
        using var host = await TestHost.StartAsync();
        await JournalAddAsync(host, "1800 2000 вечер");
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1700 работа");
        await EditAsync(host, vm.Rows[0], new TimeSpan(9, 0, 0), new TimeSpan(19, 0, 0));
        Assert.True(vm.Rows[0].IsDifferentInStatistics);
        await WaitAsync(() => JournalEntries(host).Count == 2);

        host.Interaction.OnEdit = async editor =>
        {
            Assert.Equal("Изменить запись", editor.Title); // диалог «Журнала»: меняет запись
            Assert.False(editor.HasHint);
            editor.Start = new TimeSpan(8, 0, 0);
            editor.End = new TimeSpan(16, 0, 0);
            editor.Comment = "из журнала";
            return await editor.SaveAsync();
        };
        await host.Journal.EditEntryCommand.ExecuteAsync(JournalEntries(host).Single(r => r.Comment == "работа"));

        await WaitAsync(() => vm.Rows.Count == 1 && vm.Rows[0].Comment == "из журнала");
        var row = vm.Rows[0];
        Assert.Equal("08:00 – 16:00", row.TimeText);
        Assert.True(row.IsInStatistics); // последняя правка — в «Журнале»
        Assert.Equal("В блокноте: 8 ч 00 мин · 1 запись · ведётся с 10 нояб., 14:37", vm.SummaryText);
    }

    // ---------- S22: удаление в «Журнале» ----------

    [AvaloniaFact]
    public async Task S22_DeleteInJournal_LinkedLineGoesAndReturns_FreeLineStays()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "1100 1300 связанная");
        await AddAsync(vm, "1100 1300 только блокнот");
        Assert.Equal([true, false], vm.Rows.Select(r => r.IsInStatistics));
        await WaitAsync(() => JournalEntries(host).Count == 1);

        await host.Journal.DeleteEntryCommand.ExecuteAsync(JournalEntries(host).Single());

        await WaitAsync(() => vm.Rows.Count == 1);
        var free = vm.Rows[0];
        Assert.Equal("только блокнот", free.Comment);
        Assert.True(free.IsNotInStatistics); // не стала связанной сама по себе (F9.7)
        Assert.Equal("В статистику не записано: при добавлении было пересечение", free.StatusHint);
        Assert.Contains("Итого=0:00", await StatsAsync(host), StringComparison.Ordinal);

        await host.Journal.Undo.UndoCommand.ExecuteAsync(null);

        await WaitAsync(() => vm.Rows.Count == 2);
        Assert.Equal(["связанная", "только блокнот"], vm.Rows.Select(r => r.Comment));
        Assert.Equal([true, false], vm.Rows.Select(r => r.IsInStatistics));
        Assert.Equal("Пересекается с записью 10.11 11:00–13:00 «связанная» — в статистику не записано", vm.Rows[1].StatusHint);
    }

    // ---------- S23–S24: многострочная вставка ----------

    [AvaloniaFact]
    public async Task S23_MultilinePaste_OverlapWithEarlierLineOfSameBatch()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;

        await AddAsync(vm, "0900 1200\n1100 1300\n1300 1500");

        Assert.Equal([true, false, true], vm.Rows.Select(r => r.IsInStatistics));
        Assert.Equal("Добавлено: 3 записи · не в статистике: 1 (пересечение с журналом)", vm.StatusText);
        Assert.StartsWith("В блокноте: 7 ч 00 мин · в статистике: 5 ч 00 мин · 3 записи", vm.SummaryText, StringComparison.Ordinal);
        Assert.Equal(2, (await host.Repository.GetAllAsync()).Count);
    }

    [AvaloniaFact]
    public async Task S24_MultilinePaste_WithFormatError_AddsNothing()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        var dump = await EntriesDumpAsync(host);

        vm.Input.QuickInput = "0900 1000\n1000 25:00\n1100 1200";
        Assert.True(vm.Input.PreviewIsError);
        Assert.StartsWith("✗ Строка 2:", vm.Input.PreviewText, StringComparison.Ordinal);
        Assert.False(vm.Input.AddCommand.CanExecute(null));
        var bypass = await host.NotepadService.AddFromTextAsync(vm.Input.QuickInput, host.Today); // и в обход кнопки
        Assert.False(bypass.IsSuccess);

        await vm.LoadAsync();
        Assert.True(vm.IsEmpty);
        Assert.Null(await host.NotepadService.GetCurrentAsync()); // ни строк, ни листа
        Assert.Equal(dump, await EntriesDumpAsync(host));
    }

    // ---------- S25–S26: очистка и перезапуск ----------

    [AvaloniaFact]
    public async Task S25_ClearWithMixedStatuses_ArchiveShowsStatusesAndBothSums()
    {
        using var host = await TestHost.StartAsync();
        await JournalAddAsync(host, "1800 2000 вечер");
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1700 работа\n1000 1100 прикидка\n1200 1300 обед?");
        await EditAsync(host, vm.Rows[0], new TimeSpan(9, 0, 0), new TimeSpan(19, 0, 0));
        Assert.Equal([NotepadLineStatus.DifferentInStatistics, NotepadLineStatus.NotInStatistics, NotepadLineStatus.NotInStatistics], vm.Rows.Select(r => r.Status));
        var stats = await StatsAsync(host);
        var dump = await EntriesDumpAsync(host);

        await vm.ClearAllCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.ArchiveSheets);
        Assert.Equal("12 ч 00 мин · в статистике: 8 ч 00 мин · 3 записи", card.TotalsText);
        Assert.Equal(["в статистике: 09:00–17:00", "не в статистике", "не в статистике"], card.Entries.Select(r => r.StatusLabel));
        Assert.Equal(stats, await StatsAsync(host));
        Assert.Equal(dump, await EntriesDumpAsync(host));

        await vm.Undo.UndoCommand.ExecuteAsync(null); // отмена очистки возвращает статусы как были
        Assert.Equal([NotepadLineStatus.DifferentInStatistics, NotepadLineStatus.NotInStatistics, NotepadLineStatus.NotInStatistics], vm.Rows.Select(r => r.Status));
        Assert.Equal(dump, await EntriesDumpAsync(host));
    }

    [AvaloniaFact]
    public async Task S26_Restart_KeepsLinesStatusesAndSums()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wtt-app-tests", Guid.NewGuid().ToString("N"));
        using var first = await TestHost.StartAsync(dataDir: dir);
        await JournalAddAsync(first, "1800 2000 вечер");
        await AddAsync(first.Notepad, "0900 1700 работа\n1000 1100 прикидка\n1300 1400 обед");
        await EditAsync(first, first.Notepad.Rows[0], new TimeSpan(9, 0, 0), new TimeSpan(19, 0, 0));
        await first.Notepad.ClearAllCommand.ExecuteAsync(null);
        await AddAsync(first.Notepad, "1700 1730\n1720 1740");
        var summary = first.Notepad.SummaryText;
        var labels = first.Notepad.Rows.Select(r => r.StatusLabel).ToList();
        var archive = first.Notepad.ArchiveSheets[0].Entries.Select(r => (r.TimeText, r.StatusLabel, r.StatusHint)).ToList();
        var archiveTotals = first.Notepad.ArchiveSheets[0].TotalsText;
        first.Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var second = await TestHost.StartAsync(dataDir: dir, keepExisting: true);
        var vm = second.Notepad;

        Assert.Equal(summary, vm.SummaryText);
        Assert.Equal(labels, vm.Rows.Select(r => r.StatusLabel));
        Assert.Equal(["", "не в статистике"], labels);
        Assert.Equal(archive, vm.ArchiveSheets[0].Entries.Select(r => (r.TimeText, r.StatusLabel, r.StatusHint)));
        Assert.Equal(archiveTotals, vm.ArchiveSheets[0].TotalsText);
        Assert.Equal("12 ч 00 мин · в статистике: 8 ч 00 мин · 3 записи", archiveTotals);
    }

    // ---------- Прочее ----------

    [AvaloniaFact]
    public async Task EditLine_Cancelled_Or_FormatError_ChangesNothing()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000");
        vm.StatusText = "";

        host.Interaction.OnEdit = async editor =>
        {
            editor.Date = new DateTime(2026, 11, 11); // будущее — ошибка формата остаётся ошибкой
            Assert.False(editor.CanSave);
            Assert.False(await editor.SaveAsync());
            Assert.Equal("Дата в будущем", editor.Error);
            return false;
        };
        await vm.EditEntryCommand.ExecuteAsync(vm.Rows[0]);

        Assert.Equal("09:00 – 10:00", vm.Rows[0].TimeText);
        Assert.Equal("", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task OnlyFreeLinesAdded_JournalUndoStaysAvailable()
    {
        using var host = await TestHost.StartAsync();
        await JournalAddAsync(host, "0900 1200 a");
        await JournalAddAsync(host, "1300 1400 b");
        await host.Journal.DeleteEntryCommand.ExecuteAsync(JournalEntries(host).Single(r => r.Comment == "b"));
        Assert.True(host.Journal.Undo.UndoCommand.CanExecute(null));

        await AddAsync(host.Notepad, "1000 1100"); // пересекается с «a» — в базу не пишется

        Assert.True(host.Notepad.Rows[0].IsNotInStatistics);
        Assert.True(host.Journal.Undo.UndoCommand.CanExecute(null)); // записи не изменились — отмена удаления в «Журнале» безопасна
    }

    [AvaloniaFact]
    public async Task Archive_Row_AccessibleNameIncludesStatus()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Notepad;
        await AddAsync(vm, "0900 1000 a\n0930 1000 b");

        Assert.Equal("10 нояб., 09:00 – 10:00, 1 ч 00 мин, a", vm.Rows[0].AccessibleName);
        Assert.Equal("10 нояб., 09:30 – 10:00, 0 ч 30 мин, b, не в статистике", vm.Rows[1].AccessibleName);
        Assert.StartsWith("Итого=1:00", await StatsAsync(host), StringComparison.Ordinal);
    }
}
