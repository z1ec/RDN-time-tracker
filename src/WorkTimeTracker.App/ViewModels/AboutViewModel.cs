using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>Экран «О программе» (F7.5).</summary>
public sealed partial class AboutViewModel(DataPaths paths, IUserInteraction interaction)
{
    public string Title => "WorkTime Tracker";

    public string Version => $"Версия {AppInfo.Version}";

    public string Description => "Трекер рабочего времени для Windows на C# / .NET 10 / Avalonia — pet-проект.";

    public string RepositoryUrl => AppInfo.RepositoryUrl;

    public string DataDirectory => paths.DataDirectory;

    public string License => "Лицензия MIT. Иконки — Fluent UI System Icons (MIT), шрифт Inter (SIL OFL 1.1).";

    public string Privacy => "Работает офлайн: без телеметрии, без автообновлений, без прав администратора.";

    [RelayCommand]
    private void OpenRepository() => interaction.OpenUrl(RepositoryUrl);

    [RelayCommand]
    private void OpenDataFolder() => interaction.OpenFolder(paths.DataDirectory);
}
