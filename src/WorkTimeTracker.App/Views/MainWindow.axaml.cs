using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Горячие клавиши, которым нужен доступ к визуальному дереву (F7.6): Ctrl+F, Ctrl+Z.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || e.KeyModifiers != KeyModifiers.Control)
        {
            return;
        }

        if (e.Key == Key.F)
        {
            vm.SelectedNavigation = vm.Navigation[0];
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<JournalView>().FirstOrDefault()?.FocusSearch());
            e.Handled = true;
        }
        else if (e.Key == Key.Z && vm.ActiveUndo is { } undo && undo.UndoCommand.CanExecute(null))
        {
            // Отменяется последнее действие активной вкладки (F8.20).
            // В непустом текстовом поле Ctrl+Z остаётся отменой ввода.
            if (FocusManager?.GetFocusedElement() is TextBox { Text.Length: > 0 })
            {
                return;
            }

            undo.UndoCommand.Execute(null);
            e.Handled = true;
        }
    }
}
