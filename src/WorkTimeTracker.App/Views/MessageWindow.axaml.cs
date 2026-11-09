using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;

namespace WorkTimeTracker.App.Views;

/// <summary>Дружелюбное сообщение об ошибке (F4.7, F7.10) с необязательной кнопкой «Открыть папку».</summary>
public partial class MessageWindow : Window
{
    public MessageWindow()
        : this("", "", null)
    {
    }

    public MessageWindow(string title, string message, string? folder)
    {
        InitializeComponent();
        CloseCommand = new RelayCommand(Close);
        HeaderText.Text = title;
        MessageText.Text = message;
        OpenFolderButton.IsVisible = folder is not null;
        OpenFolderButton.Click += (_, _) =>
        {
            if (folder is not null)
            {
                ShellLauncher.Open(folder);
            }
        };
    }

    public IRelayCommand CloseCommand { get; }
}
