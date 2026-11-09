using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Views;

/// <summary>Диалог редактирования записи (F6.3). Закрывается с <c>true</c>, если запись сохранена.</summary>
public partial class EditEntryWindow : Window
{
    public EditEntryWindow()
    {
        InitializeComponent();
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        if (DataContext is EntryEditorViewModel editor && await editor.SaveAsync())
        {
            Close(true);
        }
    }
}
