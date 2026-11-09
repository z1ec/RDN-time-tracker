using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Views;

public partial class JournalView : UserControl
{
    public JournalView()
    {
        InitializeComponent();
        QuickInputBox.AddHandler(KeyDownEvent, OnQuickInputKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => FocusQuickInput();
    }

    private JournalViewModel? ViewModel => DataContext as JournalViewModel;

    /// <summary>Фокус в поле быстрого ввода — при запуске и при возврате на экран (F1).</summary>
    public void FocusQuickInput() => Avalonia.Threading.Dispatcher.UIThread.Post(() => QuickInputBox.Focus());

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Enter — добавить (F7.6), Shift+Enter — новая строка, Esc — очистить поле.
    /// Поле многострочное, чтобы вставка нескольких строк работала (F1.2).
    /// </summary>
    private void OnQuickInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (vm.AddCommand.CanExecute(null))
            {
                vm.AddCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.ClearInputCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EntryRow row } && ViewModel is { } vm)
        {
            vm.EditEntryCommand.Execute(row);
        }
    }
}
