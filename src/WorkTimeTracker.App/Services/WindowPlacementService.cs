using Avalonia;
using Avalonia.Controls;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.App.Services;

/// <summary>Запоминает размер и положение окна (F4.8, P2).</summary>
public static class WindowPlacementService
{
    public static void Restore(Window window, SettingsService settings)
    {
        if (settings.Current.Window is not { } p)
        {
            return;
        }

        window.Width = Math.Max(window.MinWidth, p.Width);
        window.Height = Math.Max(window.MinHeight, p.Height);
        var position = new PixelPoint(p.X, p.Y);

        // Не восстанавливаем позицию за пределами текущих мониторов (например, отключили второй экран).
        var screens = window.Screens?.All;
        if (screens is { Count: > 0 } && screens.Any(s => s.WorkingArea.Contains(position + new PixelPoint(40, 40))))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = position;
        }

        if (p.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public static void Save(Window window, SettingsService settings)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        var size = window.ClientSize;
        var placement = maximized && settings.Current.Window is { } old
            ? old with { IsMaximized = true }
            : new WindowPlacement(window.Position.X, window.Position.Y, (int)size.Width, (int)size.Height, maximized);
        try
        {
            settings.UpdateAsync(s => s with { Window = placement }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Не удалось сохранить положение окна");
        }
    }
}
