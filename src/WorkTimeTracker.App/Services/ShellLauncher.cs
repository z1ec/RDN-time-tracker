using System.Diagnostics;

namespace WorkTimeTracker.App.Services;

/// <summary>Открытие папок и ссылок средствами ОС.</summary>
public static class ShellLauncher
{
    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", [target]);
            }
            else
            {
                Process.Start("xdg-open", [target]);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Serilog.Log.Warning(ex, "Не удалось открыть {Target}", target);
        }
    }
}
