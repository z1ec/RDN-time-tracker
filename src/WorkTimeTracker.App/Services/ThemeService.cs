using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.App.Services;

/// <summary>Применяет выбранную тему (F7.8): системная, светлая или тёмная.</summary>
public sealed class ThemeService
{
    public static ThemeVariant ToVariant(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public void Apply(ThemePreference theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        // Тема — свойство UI-объекта: меняем только из UI-потока.
        if (Dispatcher.UIThread.CheckAccess())
        {
            app.RequestedThemeVariant = ToVariant(theme);
        }
        else
        {
            Dispatcher.UIThread.Post(() => app.RequestedThemeVariant = ToVariant(theme));
        }
    }
}
