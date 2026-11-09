using WorkTimeTracker.App.ViewModels;

namespace WorkTimeTracker.App.Services;

/// <summary>
/// Всё, что требует настоящего окна или ОС: диалоги, выбор файла, открытие папок и ссылок.
/// В тестах ViewModel подменяется фейком.
/// </summary>
public interface IUserInteraction
{
    /// <summary>Показывает диалог редактирования записи. Возвращает <c>true</c>, если запись сохранена.</summary>
    Task<bool> EditEntryAsync(EntryEditorViewModel editor);

    /// <summary>Спрашивает путь для сохранения файла. <c>null</c> — пользователь отменил.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName);

    /// <summary>Показывает дружелюбное сообщение об ошибке; если задана папка — с кнопкой «Открыть папку».</summary>
    Task ShowErrorAsync(string title, string message, string? folderToOpen = null);

    void OpenFolder(string path);

    void OpenUrl(string url);
}
