using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App.Services;

/// <summary>Реализация <see cref="IUserInteraction"/> на окнах Avalonia.</summary>
public sealed class UserInteraction(Func<Window?> owner) : IUserInteraction
{
    public async Task<bool> EditEntryAsync(EntryEditorViewModel editor)
    {
        var window = new EditEntryWindow { DataContext = editor };
        return owner() is { } o ? await window.ShowDialog<bool>(o) : false;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName)
    {
        if (owner() is not { } o)
        {
            return null;
        }

        var file = await o.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("CSV (Excel)") { Patterns = ["*.csv"], MimeTypes = ["text/csv"] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task ShowErrorAsync(string title, string message, string? folderToOpen = null)
    {
        var window = new MessageWindow(title, message, folderToOpen);
        if (owner() is { IsVisible: true } o)
        {
            await window.ShowDialog(o);
        }
        else
        {
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Show();
            await closed.Task;
        }
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        ShellLauncher.Open(path);
    }

    public void OpenUrl(string url) => ShellLauncher.Open(url);
}
