using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App.Tests;

/// <summary>Headless UI smoke (настоящее окно, клавиатура, привязки).</summary>
public class HeadlessSmokeTests
{
    [AvaloniaFact]
    public async Task Window_Opens_TypeAndEnter_AddsRow()
    {
        using var host = await TestHost.StartAsync();
        var window = await UiTestHelpers.ShowMainWindowAsync(host);
        try
        {
            Assert.Equal("WorkTime Tracker", window.Title);
            Assert.Equal(900, window.MinWidth);
            Assert.Equal(600, window.MinHeight);
            var input = window.FindByAutomationName<TextBox>("Быстрый ввод");
            await UiTestHelpers.WaitForAsync(() => input.IsFocused);

            window.KeyTextInput("0900 1800 смоук-тест");
            await UiTestHelpers.SettleAsync();
            Assert.Equal("✓ 10 нояб. · 09:00 → 18:00 · 9 ч 00 мин · «смоук-тест»", host.Journal.Input.PreviewText);

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await UiTestHelpers.WaitForAsync(() => host.Journal.Rows.OfType<EntryRow>().Any());

            Assert.Equal("", input.Text);
            var list = window.FindByAutomationName<ListBox>("Записи");
            await UiTestHelpers.WaitForAsync(() => list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "смоук-тест"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShiftEnter_AddsNewLine_Escape_Clears()
    {
        using var host = await TestHost.StartAsync();
        var window = await UiTestHelpers.ShowMainWindowAsync(host);
        try
        {
            var input = window.FindByAutomationName<TextBox>("Быстрый ввод");
            input.Focus();
            window.KeyTextInput("0900 1000");
            window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
            window.KeyTextInput("1100 1200");
            await UiTestHelpers.SettleAsync();
            Assert.Equal("✓ 2 записи · всего 2 ч 00 мин", host.Journal.Input.PreviewText);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await UiTestHelpers.SettleAsync();
            Assert.Equal("", host.Journal.Input.QuickInput);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Hotkeys_Navigate_Delete_Undo_Search()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0900 1000 первая\n1100 1200 вторая";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        var window = await UiTestHelpers.ShowMainWindowAsync(host);
        try
        {
            window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, "2");
            await UiTestHelpers.SettleAsync();
            Assert.Same(host.Notepad, host.Main.CurrentPage); // F8.1: Ctrl+2 — «Блокнот»
            window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, "3");
            await UiTestHelpers.SettleAsync();
            Assert.Same(host.Statistics, host.Main.CurrentPage);
            window.KeyPress(Key.D5, RawInputModifiers.Control, PhysicalKey.Digit5, "5");
            await UiTestHelpers.SettleAsync();
            Assert.IsType<AboutViewModel>(host.Main.CurrentPage);
            window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, "1");
            await UiTestHelpers.SettleAsync();
            Assert.Same(host.Journal, host.Main.CurrentPage);

            var list = window.FindByAutomationName<ListBox>("Записи");
            var first = host.Journal.Rows.OfType<EntryRow>().First();
            list.SelectedItem = first;
            await UiTestHelpers.SettleAsync();
            Assert.True(list.ContainerFromItem(first)!.Focus()); // как после клика по строке
            await UiTestHelpers.SettleAsync();
            Assert.Single(host.Journal.SelectedRows); // SelectedItems ListBox связан с ViewModel
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            await UiTestHelpers.WaitForAsync(() => host.Journal.Undo.IsVisible);
            Assert.Single(host.Journal.Rows.OfType<EntryRow>());

            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            await UiTestHelpers.WaitForAsync(() => host.Journal.Rows.OfType<EntryRow>().Count() == 2);

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            await UiTestHelpers.SettleAsync();
            var search = window.FindByAutomationName<TextBox>("Поиск по комментариям");
            Assert.True(search.IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AllPages_Render()
    {
        using var host = await TestHost.StartAsync(demoData: true);
        var window = await UiTestHelpers.ShowMainWindowAsync(host);
        try
        {
            foreach (var page in new[] { "2", "3", "4", "5", "1" })
            {
                host.Main.NavigateCommand.Execute(page);
                await UiTestHelpers.SettleAsync();
                Assert.NotNull(window.CaptureRenderedFrame());
            }

            Assert.NotNull(window.FindDescendant<JournalView>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Dialogs_Render()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0900 1000";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        var owner = await UiTestHelpers.ShowMainWindowAsync(host);
        try
        {
            var editor = new EntryEditorViewModel(host.Journal.Rows.OfType<EntryRow>().Single().Entry, host.Entries);
            var dialog = new EditEntryWindow { DataContext = editor };
            var result = dialog.ShowDialog<bool>(owner);
            await UiTestHelpers.SettleAsync();
            Assert.NotNull(dialog.CaptureRenderedFrame());
            editor.Comment = "из диалога";
            await dialog.SaveCommand.ExecuteAsync(null);
            Assert.True(await result);

            var message = new MessageWindow("Заголовок", "Текст", host.Dir);
            var closed = message.ShowDialog(owner);
            await UiTestHelpers.SettleAsync();
            message.CloseCommand.Execute(null);
            await closed;

            var cancel = new EditEntryWindow { DataContext = editor };
            var cancelled = cancel.ShowDialog<bool>(owner);
            await UiTestHelpers.SettleAsync();
            cancel.CancelCommand.Execute(null);
            Assert.False(await cancelled);
        }
        finally
        {
            owner.Close();
        }
    }
}
