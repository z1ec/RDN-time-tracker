using Avalonia.Headless.XUnit;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.App.Tests;

/// <summary>ViewModel «Журнала»: F1, F2, F3.4, F5, F6, F7.2. «Сегодня» — вторник 10.11.2026 14:37.</summary>
public class JournalViewModelTests
{
    [AvaloniaFact]
    public async Task Preview_Valid_Error_Multiline_Empty()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;

        vm.QuickInput = "0900 1800 созвон";
        Assert.Equal("✓ 10 нояб. · 09:00 → 18:00 · 9 ч 00 мин · «созвон»", vm.PreviewText);
        Assert.False(vm.PreviewIsError);
        Assert.True(vm.AddCommand.CanExecute(null));

        vm.QuickInput = "2200 0200";
        Assert.Equal("✓ 10 нояб. · 22:00 → 02:00 +1 · 4 ч 00 мин", vm.PreviewText);

        vm.QuickInput = "2500 1800";
        Assert.Equal("✗ Часы должны быть от 0 до 23 — «2500»", vm.PreviewText);
        Assert.True(vm.PreviewIsError);
        Assert.False(vm.AddCommand.CanExecute(null));

        vm.QuickInput = "0900 1300\n1400 1800\n1900 2000";
        Assert.Equal("✓ 3 записи · всего 9 ч 00 мин", vm.PreviewText);

        vm.QuickInput = "0900 1300\nabc";
        Assert.Equal("✗ Строка 2: Не удалось распознать время начала — «abc»", vm.PreviewText);

        vm.QuickInput = "   ";
        Assert.Equal("", vm.PreviewText);
        Assert.False(vm.HasPreview);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Add_FromQuickInput_UpdatesListAndSummary()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        Assert.True(vm.IsEmpty);
        Assert.Equal("Пока нет записей — введите, например, 0900 1800 планёрка", vm.EmptyText);
        Assert.Equal("Сегодня: 0 ч 00 мин · Неделя: 0 ч 00 мин из 40 ч", vm.SummaryText);

        vm.QuickInput = "0900 1800 работа";
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal("", vm.QuickInput);
        Assert.False(vm.IsEmpty);
        Assert.Equal("Добавлено: 1 запись", vm.StatusText);
        var header = Assert.IsType<DayHeaderRow>(vm.Rows[0]);
        Assert.Equal("Вт, 10 ноября", header.Title);
        Assert.Equal("9 ч 00 мин", header.TotalText);
        Assert.True(header.IsToday);
        Assert.False(header.IsSelectable);
        var row = Assert.IsType<EntryRow>(vm.Rows[1]);
        Assert.Equal("09:00 – 18:00", row.TimeText);
        Assert.Equal("9 ч 00 мин", row.DurationText);
        Assert.Equal("работа", row.Comment);
        Assert.True(row.IsSelectable);
        Assert.Equal("09:00 – 18:00, 9 ч 00 мин, работа", row.AccessibleName);
        Assert.Equal("Сегодня: 9 ч 00 мин · Неделя: 9 ч 00 мин из 40 ч", vm.SummaryText);
    }

    [AvaloniaFact]
    public async Task Add_Multiline_IsAtomic_AndShowsOverlap()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "1300 1500 обед";
        await vm.AddCommand.ExecuteAsync(null);

        vm.QuickInput = "0900 1000\n1400 1600";
        await vm.AddCommand.ExecuteAsync(null);

        Assert.True(vm.PreviewIsError);
        Assert.Equal("✗ Строка 2: Пересекается с записью 10.11 13:00–15:00 «обед»", vm.PreviewText);
        Assert.Equal("0900 1000\n1400 1600", vm.QuickInput); // ввод не теряется
        Assert.Single(await host.Repository.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task SelectedDate_IsUsedForInputWithoutDate()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        Assert.Equal("Сегодня", vm.SelectedDateLabel);
        vm.SelectedDate = new DateTime(2026, 11, 2);
        Assert.Equal("2 нояб.", vm.SelectedDateLabel);
        vm.QuickInput = "0900 1000";
        Assert.StartsWith("✓ 2 нояб.", vm.PreviewText, StringComparison.Ordinal);
        await vm.AddCommand.ExecuteAsync(null);
        Assert.Equal(new DateOnly(2026, 11, 2), (await host.Repository.GetAllAsync(TestContext.Current.CancellationToken))[0].Date);
        vm.ResetDateCommand.Execute(null);
        Assert.Equal(host.Today, vm.SelectedDay);
    }

    [AvaloniaFact]
    public async Task ClearInput_ByEscape()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.QuickInput = "0900";
        host.Journal.ClearInputCommand.Execute(null);
        Assert.Equal("", host.Journal.QuickInput);
        Assert.False(host.Journal.HasPreview);
    }

    [AvaloniaFact]
    public async Task Form_NightShift_LiveDuration_AndAdd()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        Assert.False(vm.AddFromFormCommand.CanExecute(null));

        vm.FormStart = new TimeSpan(22, 0, 0);
        vm.FormEnd = new TimeSpan(2, 0, 0);
        vm.FormComment = "ночь";
        Assert.Equal("4 ч 00 мин · +1 день", vm.FormDurationText);
        Assert.Equal("4/500", vm.FormCommentCounter);
        Assert.True(vm.AddFromFormCommand.CanExecute(null));

        await vm.AddFromFormCommand.ExecuteAsync(null);

        Assert.Null(vm.FormStart);
        Assert.Equal("", vm.FormComment);
        var entry = (await host.Repository.GetAllAsync(TestContext.Current.CancellationToken))[0];
        Assert.Equal((1320, 1560, "ночь"), (entry.StartMinute, entry.EndMinute, entry.Comment));
        var row = vm.Rows.OfType<EntryRow>().Single();
        Assert.True(row.EndsNextDay);
        Assert.Equal("22:00 – 02:00", row.TimeText);
    }

    [AvaloniaFact]
    public async Task Form_Errors_SameAsQuickInput()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.FormStart = new TimeSpan(9, 0, 0);
        vm.FormEnd = new TimeSpan(9, 0, 0);
        Assert.Equal("Нулевая длительность", vm.FormError);
        Assert.False(vm.AddFromFormCommand.CanExecute(null));

        vm.QuickInput = "0800 1000";
        await vm.AddCommand.ExecuteAsync(null);
        vm.FormEnd = new TimeSpan(11, 0, 0);
        await vm.AddFromFormCommand.ExecuteAsync(null);
        Assert.Equal("Пересекается с записью 10.11 08:00–10:00", vm.FormError);
    }

    [AvaloniaFact]
    public async Task Form_NowButtons_UseClock()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.SetFormStartNowCommand.Execute(null);
        host.Journal.SetFormEndNowCommand.Execute(null);
        Assert.Equal(new TimeSpan(14, 37, 0), host.Journal.FormStart);
        Assert.Equal(new TimeSpan(14, 37, 0), host.Journal.FormEnd);
    }

    [AvaloniaFact]
    public async Task Delete_Toast_Undo()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "0900 1000 a\n1100 1200 b\n1300 1400 c";
        await vm.AddCommand.ExecuteAsync(null);
        foreach (var row in vm.Rows.OfType<EntryRow>().Take(2).ToList())
        {
            vm.SelectedRows.Add(row);
        }

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Single(vm.Rows.OfType<EntryRow>());
        Assert.True(vm.IsUndoToastVisible);
        Assert.Equal("Удалено записей: 2", vm.UndoToastText);
        Assert.True(vm.UndoCommand.CanExecute(null));

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Rows.OfType<EntryRow>().Count());
        Assert.False(vm.IsUndoToastVisible);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Equal("Восстановлено: 2 записи", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Toast_HidesAfter8Seconds_UndoStillAvailable()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "0900 1000";
        await vm.AddCommand.ExecuteAsync(null);
        await vm.DeleteEntryCommand.ExecuteAsync(vm.Rows.OfType<EntryRow>().Single());
        Assert.True(vm.IsUndoToastVisible);

        host.Time.Advance(TimeSpan.FromSeconds(7));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(vm.IsUndoToastVisible);
        host.Time.Advance(TimeSpan.FromSeconds(1));
        await WaitAsync(() => !vm.IsUndoToastVisible);

        Assert.True(vm.UndoCommand.CanExecute(null)); // Ctrl+Z работает и после скрытия уведомления
    }

    [AvaloniaFact]
    public async Task Undo_UnavailableAfterNextChange()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "0900 1000";
        await vm.AddCommand.ExecuteAsync(null);
        await vm.DeleteEntryCommand.ExecuteAsync(vm.Rows.OfType<EntryRow>().Single());

        vm.QuickInput = "0930 1030";
        await vm.AddCommand.ExecuteAsync(null);

        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.IsUndoToastVisible);
    }

    [AvaloniaFact]
    public async Task Delete_NothingSelected_DoesNothing()
    {
        using var host = await TestHost.StartAsync();
        await host.Journal.DeleteSelectedCommand.ExecuteAsync(null);
        await host.Journal.DeleteEntryCommand.ExecuteAsync(null);
        Assert.False(host.Journal.IsUndoToastVisible);
    }

    [AvaloniaFact]
    public async Task Edit_ThroughDialog()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "0900 1000 a\n1100 1200 b";
        await vm.AddCommand.ExecuteAsync(null);
        var first = vm.Rows.OfType<EntryRow>().First();

        EntryEditorViewModel? seen = null;
        host.Interaction.OnEdit = async editor =>
        {
            seen = editor;
            Assert.Equal(new DateTime(2026, 11, 10), editor.Date);
            Assert.Equal(new TimeSpan(9, 0, 0), editor.Start);
            Assert.Equal("1 ч 00 мин", editor.DurationText);
            editor.End = new TimeSpan(11, 30, 0);       // пересечётся с «b»
            Assert.True(editor.CanSave);
            Assert.False(await editor.SaveAsync());
            Assert.Equal("Пересекается с записью 10.11 11:00–12:00 «b»", editor.Error);
            editor.End = new TimeSpan(10, 30, 0);
            editor.Comment = "a2";
            Assert.Equal("2/500", editor.CommentCounter);
            return await editor.SaveAsync();
        };

        await vm.EditEntryCommand.ExecuteAsync(first);

        Assert.NotNull(seen);
        Assert.Equal("Запись изменена", vm.StatusText);
        var updated = vm.Rows.OfType<EntryRow>().First();
        Assert.Equal(("09:00 – 10:30", "a2"), (updated.TimeText, updated.Comment));
    }

    [AvaloniaFact]
    public async Task Editor_Validation()
    {
        using var host = await TestHost.StartAsync();
        var editor = new EntryEditorViewModel(new WorkEntry(1, host.Today, 1320, 1560, "ночь"), host.Entries);
        Assert.Equal(new TimeSpan(2, 0, 0), editor.End);
        Assert.Equal("4 ч 00 мин · +1 день", editor.DurationText);
        editor.Start = null;
        Assert.False(editor.CanSave);
        Assert.Equal("Укажите дату, начало и конец", editor.Error);
        Assert.False(await editor.SaveAsync());
        editor.Start = new TimeSpan(22, 0, 0);
        editor.Date = new DateTime(2026, 11, 11);
        Assert.Equal("Дата в будущем", editor.Error);
        Assert.Equal(new DateTime(2026, 11, 10), editor.MaxDate.Date);
    }

    [AvaloniaFact]
    public async Task Editor_FullDayEntry_CanBeSaved()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.QuickInput = "0000 2400 дежурство";
        await host.Journal.AddCommand.ExecuteAsync(null);
        var editor = new EntryEditorViewModel(host.Journal.Rows.OfType<EntryRow>().Single().Entry, host.Entries);
        Assert.True(editor.CanSave);
        Assert.Equal("24 ч 00 мин", editor.DurationText);
        editor.Comment = "дежурство сутки";
        Assert.True(await editor.SaveAsync());
        var saved = (await host.Repository.GetAllAsync(TestContext.Current.CancellationToken)).Single();
        Assert.Equal((0, 1440, "дежурство сутки"), (saved.StartMinute, saved.EndMinute, saved.Comment));
    }

    [AvaloniaFact]
    public async Task Edit_CancelledOrNull_DoesNothing()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "0900 1000";
        await vm.AddCommand.ExecuteAsync(null);
        await vm.EditEntryCommand.ExecuteAsync(vm.Rows.OfType<EntryRow>().Single());
        await vm.EditEntryCommand.ExecuteAsync(null);
        Assert.NotEqual("Запись изменена", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Search_Cyrillic_CaseInsensitive()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "0900 1000 Созвон с клиентом\n1100 1200 обед\n1300 1400 созвон";
        await vm.AddCommand.ExecuteAsync(null);

        vm.SearchText = "СОЗВОН";
        await WaitAsync(() => vm.Rows.OfType<EntryRow>().Count() == 2);
        Assert.True(vm.IsSearching);
        Assert.False(vm.HasMore);

        vm.SearchText = "отпуск";
        await WaitAsync(() => vm.IsEmpty);
        Assert.Equal("Ничего не найдено по запросу «отпуск»", vm.EmptyText);

        vm.ClearSearchCommand.Execute(null);
        await WaitAsync(() => vm.Rows.OfType<EntryRow>().Count() == 3);
    }

    [AvaloniaFact]
    public async Task ShowMore_LoadsOlderDays()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "01.09.2026 0900 1000 давно\n0900 1000 сегодня";
        await vm.AddCommand.ExecuteAsync(null);
        Assert.True(vm.HasMore);
        Assert.Single(vm.Rows.OfType<EntryRow>());

        await vm.ShowMoreCommand.ExecuteAsync(null);
        await vm.ShowMoreCommand.ExecuteAsync(null);

        Assert.Equal(90, vm.DaysLoaded);
        Assert.Equal(2, vm.Rows.OfType<EntryRow>().Count());
        Assert.False(vm.HasMore);
    }

    [AvaloniaFact]
    public async Task OlderYear_HeaderHasYear()
    {
        using var host = await TestHost.StartAsync();
        var vm = host.Journal;
        vm.QuickInput = "28.12 0900 1000";
        await vm.AddCommand.ExecuteAsync(null);
        vm.SearchText = " ";
        vm.DaysLoaded = 400;
        await vm.LoadAsync();
        Assert.Equal("Вс, 28 декабря 2025", vm.Rows.OfType<DayHeaderRow>().Single().Title);
    }

    [AvaloniaFact]
    public async Task SettingsChange_UpdatesSummaryNorm()
    {
        using var host = await TestHost.StartAsync();
        host.Settings.DailyNorm = new TimeSpan(7, 0, 0);
        await WaitAsync(() => host.Journal.SummaryText.EndsWith("из 35 ч", StringComparison.Ordinal));
    }

    internal static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
