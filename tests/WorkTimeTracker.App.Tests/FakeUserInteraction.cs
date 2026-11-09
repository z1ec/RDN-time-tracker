using WorkTimeTracker.App.Services;
using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Tests;

/// <summary>Подмена диалогов и ОС для тестов ViewModel.</summary>
public sealed class FakeUserInteraction : IUserInteraction
{
    public Func<EntryEditorViewModel, Task<bool>> OnEdit { get; set; } = _ => Task.FromResult(false);

    public string? SavePath { get; set; }

    public List<(string Title, string Message, string? Folder)> Errors { get; } = [];

    public List<string> OpenedFolders { get; } = [];

    public List<string> OpenedUrls { get; } = [];

    public Task<bool> EditEntryAsync(EntryEditorViewModel editor) => OnEdit(editor);

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName) => Task.FromResult(SavePath);

    public Task ShowErrorAsync(string title, string message, string? folderToOpen = null)
    {
        Errors.Add((title, message, folderToOpen));
        return Task.CompletedTask;
    }

    public void OpenFolder(string path) => OpenedFolders.Add(path);

    public void OpenUrl(string url) => OpenedUrls.Add(url);
}
