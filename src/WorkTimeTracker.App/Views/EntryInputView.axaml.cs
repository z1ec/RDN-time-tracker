using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Views;

/// <summary>Быстрый ввод и форма — общий контрол «Журнала» и «Блокнота» (F1, F2, F8.2).</summary>
public partial class EntryInputView : UserControl
{
    public EntryInputView()
    {
        InitializeComponent();
        QuickInputBox.AddHandler(KeyDownEvent, OnQuickInputKeyDown, RoutingStrategies.Tunnel);
    }

    private EntryInputViewModel? ViewModel => DataContext as EntryInputViewModel;

    /// <summary>Фокус в поле быстрого ввода — при запуске и при возврате на экран (F1).</summary>
    public void FocusQuickInput() => Avalonia.Threading.Dispatcher.UIThread.Post(() => QuickInputBox.Focus());

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
}
