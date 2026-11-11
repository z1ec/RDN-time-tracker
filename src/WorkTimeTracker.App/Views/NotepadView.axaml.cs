using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Views;

public partial class NotepadView : UserControl
{
    public NotepadView()
    {
        InitializeComponent();
        TitleBox.AddHandler(KeyDownEvent, OnTitleKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => EntryInput.FocusQuickInput();
        DetachedFromVisualTree += (_, _) => CommitTitle();
    }

    private NotepadViewModel? ViewModel => DataContext as NotepadViewModel;

    /// <summary>
    /// Название сохраняется при уходе фокуса из поля. Если вкладку закрыли, не выходя из поля
    /// (например, Ctrl+3), набранное название не должно потеряться.
    /// </summary>
    public void CommitTitle()
    {
        if (ViewModel is { } vm && (TitleBox.Text ?? "") != vm.Title)
        {
            vm.Title = TitleBox.Text ?? "";
        }
    }

    /// <summary>Enter в названии — сохранить и перейти к вводу времени.</summary>
    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitle();
            EntryInput.FocusQuickInput();
            e.Handled = true;
        }
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: NotepadLineRow row } && ViewModel is { } vm)
        {
            vm.EditEntryCommand.Execute(row);
        }
    }
}
