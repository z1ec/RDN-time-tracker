using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App.Tests;

/// <summary>Headless UI smoke вкладки «Блокнот» (F8, F9): настоящее окно, клавиатура, привязки.</summary>
public class NotepadSmokeTests
{
    private static async Task<MainWindow> OpenNotepadAsync(TestHost host)
    {
        var window = await UiTestHelpers.ShowMainWindowAsync(host);
        window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, "2");
        await UiTestHelpers.WaitForAsync(() => window.FindDescendant<NotepadView>() is not null);
        return window;
    }

    [AvaloniaFact]
    public async Task CtrlTwo_OpensNotepad_TypeAndEnter_AddsRowAndSum()
    {
        using var host = await TestHost.StartAsync();
        var window = await OpenNotepadAsync(host);
        try
        {
            Assert.Same(host.Notepad, host.Main.CurrentPage);
            Assert.Same(host.Main.Navigation[1], host.Main.SelectedNavigation);
            var empty = window.FindByAutomationName<TextBlock>("Блокнот пуст");
            Assert.True(empty.IsEffectivelyVisible);
            Assert.False(window.FindByAutomationName<Button>("Очистить всё").IsEffectivelyEnabled);
            var input = window.FindByAutomationName<TextBox>("Быстрый ввод"); // тот же контрол, что в «Журнале»
            await UiTestHelpers.WaitForAsync(() => input.IsFocused);

            window.KeyTextInput("0900 1800");
            await UiTestHelpers.SettleAsync();
            Assert.Equal("✓ 10 нояб. · 09:00 → 18:00 · 9 ч 00 мин", host.Notepad.Input.PreviewText);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 1);

            Assert.Equal("", input.Text);
            var list = window.FindByAutomationName<ListBox>("Записи блокнота");
            await UiTestHelpers.WaitForAsync(() => list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "09:00 – 18:00"));
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "10 нояб.");
            var summary = window.FindByAutomationName<SelectableTextBlock>("Сумма блокнота");
            Assert.Equal("В блокноте: 9 ч 00 мин · 1 запись · ведётся с 10 нояб., 14:37", summary.Text);
            Assert.False(empty.IsEffectivelyVisible);
            Assert.True(window.FindByAutomationName<Button>("Очистить всё").IsEffectivelyEnabled);
            Assert.Single(await host.Repository.GetAllAsync()); // запись — обычная запись журнала
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Delete_RemovesOnlyFromNotepad_CtrlZ_UndoesOnActiveTab()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0700 0800 из журнала";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        await host.Journal.DeleteEntryCommand.ExecuteAsync(host.Journal.Rows.OfType<EntryRow>().Single());
        host.Notepad.Input.QuickInput = "0900 1000 первая\n1100 1200 вторая";
        await host.Notepad.Input.AddCommand.ExecuteAsync(null);
        var window = await OpenNotepadAsync(host);
        try
        {
            var list = window.FindByAutomationName<ListBox>("Записи блокнота");
            var first = host.Notepad.Rows[0];
            list.SelectedItem = first;
            await UiTestHelpers.SettleAsync();
            Assert.True(list.ContainerFromItem(first)!.Focus());
            await UiTestHelpers.SettleAsync();
            Assert.Single(host.Notepad.SelectedRows);

            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            // Уведомление об отмене появляется до перезагрузки списка (RemoveAsync) — ждём и то, и другое
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Undo.IsVisible && host.Notepad.Rows.Count == 1);
            Assert.Equal(2, (await host.Repository.GetAllAsync()).Count); // записи остались
            var toast = window.FindByAutomationName<TextBlock>("Уведомление");
            Assert.Equal("Убрано из блокнота: 1 запись", toast.Text);
            Assert.True(toast.IsEffectivelyVisible);

            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 2);
            Assert.False(host.Notepad.Undo.IsVisible);

            // F8.20: в «Журнале» есть своё отменяемое удаление — Ctrl+Z на вкладке «Блокнот» его не трогает.
            window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, "1");
            await UiTestHelpers.SettleAsync();
            await host.Journal.DeleteEntryCommand.ExecuteAsync(host.Journal.Rows.OfType<EntryRow>().First());
            Assert.True(host.Journal.Undo.UndoCommand.CanExecute(null));
            window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, "2");
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 1); // удаление в журнале убрало запись и из блокнота
            window.FindByAutomationName<ListBox>("Записи блокнота").Focus();
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            await UiTestHelpers.SettleAsync();
            Assert.True(host.Journal.Undo.UndoCommand.CanExecute(null)); // не сработало: активна другая вкладка
            Assert.Single(await host.Repository.GetAllAsync());

            window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, "1");
            await UiTestHelpers.SettleAsync();
            window.FindByAutomationName<ListBox>("Записи").Focus();
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 2); // отмена в «Журнале» вернула запись и в блокнот
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>F9.8, S13 в настоящем окне: ввёл, убрал ✕, ввёл снова — строка добавлена с меткой «не в статистике».</summary>
    [AvaloniaFact]
    public async Task TypeRemoveTypeAgain_SecondLineShownNotInStatistics()
    {
        using var host = await TestHost.StartAsync();
        var window = await OpenNotepadAsync(host);
        try
        {
            var input = window.FindByAutomationName<TextBox>("Быстрый ввод");
            await UiTestHelpers.WaitForAsync(() => input.IsFocused);
            window.KeyTextInput("1100 1300");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 1);
            var list = window.FindByAutomationName<ListBox>("Записи блокнота");
            await UiTestHelpers.WaitForAsync(() => list.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Убрать из блокнота"));
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "не в статистике" && t.IsEffectivelyVisible);

            var remove = list.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Убрать из блокнота");
            remove.Command!.Execute(remove.CommandParameter); // первая операция: ✕
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 0 && host.Notepad.Undo.IsVisible);
            Assert.Equal("Убрано из блокнота: 1 запись", window.FindByAutomationName<TextBlock>("Уведомление").Text);

            input.Focus();
            window.KeyTextInput("1100 1300");
            await UiTestHelpers.SettleAsync();
            Assert.False(host.Notepad.Input.PreviewIsError); // предпросмотр не ругается на пересечение
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // вторая операция: снова ввод
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 1);

            Assert.Equal("", input.Text);
            await UiTestHelpers.WaitForAsync(() => list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "не в статистике" && t.IsEffectivelyVisible));
            var label = list.GetVisualDescendants().OfType<Border>().First(b => AutomationProperties.GetName(b) == "не в статистике" && b.IsEffectivelyVisible);
            Assert.Equal("Пересекается с записью 10.11 11:00–13:00 — в статистику не записано", ToolTip.GetTip(label));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Добавлено: 1 запись · не в статистике: 1 (пересечение с журналом)");
            var summary = window.FindByAutomationName<SelectableTextBlock>("Сумма блокнота");
            Assert.StartsWith("В блокноте: 2 ч 00 мин · в статистике: 0 ч 00 мин", summary.Text, StringComparison.Ordinal);
            Assert.True(window.FindByAutomationName<TextBlock>("Пояснение к сумме").IsEffectivelyVisible);
            Assert.Single(await host.Repository.GetAllAsync()); // в «Журнале» одна запись
            Assert.NotNull(window.CaptureRenderedFrame());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Title_Clear_Archive_RenderAndWork()
    {
        using var host = await TestHost.StartAsync();
        var window = await OpenNotepadAsync(host);
        try
        {
            var title = window.FindByAutomationName<TextBox>("Название листа");
            Assert.Equal("Без названия", title.PlaceholderText);
            Assert.Equal(100, title.MaxLength);
            title.Focus();
            window.KeyTextInput("Заказ: сайт кафе");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // Enter сохраняет и уводит фокус в быстрый ввод
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Title == "Заказ: сайт кафе");
            await host.Notepad.TitleSaveTask;
            var input = window.FindByAutomationName<TextBox>("Быстрый ввод");
            await UiTestHelpers.WaitForAsync(() => input.IsFocused);

            window.KeyTextInput("0900 1230 задача");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Rows.Count == 1);

            var clear = window.FindByAutomationName<Button>("Очистить всё");
            Assert.True(clear.IsEffectivelyEnabled);
            clear.Command!.Execute(null);
            await UiTestHelpers.WaitForAsync(() => host.Notepad.ArchiveSheets.Count == 1);
            Assert.Equal("Блокнот перенесён в архив", window.FindByAutomationName<TextBlock>("Уведомление").Text);
            Assert.Equal("", title.Text);

            var tabs = window.FindByAutomationName<ListBox>("Текущий лист или архив");
            tabs.SelectedIndex = 1;
            await UiTestHelpers.SettleAsync();
            Assert.True(host.Notepad.IsArchiveVisible);
            var archive = window.FindByAutomationName<ItemsControl>("Архив блокнота");
            Assert.True(archive.IsEffectivelyVisible);
            Assert.False(input.IsEffectivelyVisible);
            Assert.Contains(archive.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Заказ: сайт кафе");
            Assert.Contains(archive.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "10 нояб. · 3 ч 30 мин · 1 запись · очищен 10 нояб., 14:37");
            Assert.DoesNotContain(archive.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "задача" && t.IsEffectivelyVisible);

            host.Notepad.ArchiveSheets[0].ToggleExpandedCommand.Execute(null); // клик по карточке
            await UiTestHelpers.SettleAsync();
            Assert.Contains(archive.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "задача" && t.IsEffectivelyVisible);
            Assert.NotNull(window.CaptureRenderedFrame());

            window.FindByAutomationName<Button>("Удалить из архива").Command!.Execute(host.Notepad.ArchiveSheets[0]);
            await UiTestHelpers.WaitForAsync(() => host.Notepad.IsArchiveEmpty);
            Assert.True(window.FindByAutomationName<TextBlock>("Архив пуст").IsEffectivelyVisible);
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            await UiTestHelpers.WaitForAsync(() => host.Notepad.ArchiveSheets.Count == 1);
            Assert.Single(await host.Repository.GetAllAsync());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Title_NotLost_WhenLeavingTabWithoutLeavingField()
    {
        using var host = await TestHost.StartAsync();
        var window = await OpenNotepadAsync(host);
        try
        {
            window.FindByAutomationName<TextBox>("Название листа").Focus();
            window.KeyTextInput("Набрано и сразу Ctrl+3");
            window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, "3");
            await UiTestHelpers.WaitForAsync(() => host.Notepad.Title == "Набрано и сразу Ctrl+3");
            await host.Notepad.TitleSaveTask;
            Assert.Equal("Набрано и сразу Ctrl+3", (await host.NotepadService.GetCurrentAsync())!.Sheet.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Title_NotLost_WhenWindowClosedFromTitleField()
    {
        using var host = await TestHost.StartAsync();
        var window = await OpenNotepadAsync(host);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.FindByAutomationName<TextBox>("Название листа").Focus();
        window.KeyTextInput("Набрано и сразу Alt+F4");

        window.Close();

        await UiTestHelpers.WaitForAsync(() => closed); // окно закрывается только после записи названия в БД
        Assert.True(host.Notepad.TitleSaveTask.IsCompletedSuccessfully);
        Assert.Equal("Набрано и сразу Alt+F4", (await host.NotepadService.GetCurrentAsync())!.Sheet.Title);
    }

    [AvaloniaFact]
    public async Task JournalDeleteTooltip_MentionsNotepad()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0900 1000";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        var window = await UiTestHelpers.ShowMainWindowAsync(host);
        try
        {
            var delete = window.FindByAutomationName<Button>("Удалить запись");
            Assert.Equal("Удалить запись (в том числе из блокнота)", ToolTip.GetTip(delete));
            window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, "2");
            await UiTestHelpers.SettleAsync();
            host.Notepad.Input.QuickInput = "1100 1200";
            await host.Notepad.Input.AddCommand.ExecuteAsync(null);
            await UiTestHelpers.SettleAsync();
            var remove = window.FindByAutomationName<Button>("Убрать из блокнота");
            Assert.Equal("Убрать из блокнота (журнал и статистика не меняются)", ToolTip.GetTip(remove));
        }
        finally
        {
            window.Close();
        }
    }
}
