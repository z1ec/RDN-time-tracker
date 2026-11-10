using Avalonia.Controls;
using Avalonia.Input;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Views;

public partial class JournalView : UserControl
{
    public JournalView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => EntryInput.FocusQuickInput();
    }

    private JournalViewModel? ViewModel => DataContext as JournalViewModel;

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EntryRow row } && ViewModel is { } vm)
        {
            vm.EditEntryCommand.Execute(row);
        }
    }
}
