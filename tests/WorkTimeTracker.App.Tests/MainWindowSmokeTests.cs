using Avalonia.Headless.XUnit;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App.Tests;

public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_Opens()
    {
        var window = new MainWindow();
        window.Show();
        Assert.True(window.IsVisible);
        Assert.Equal("WorkTime Tracker", window.Title);
        window.Close();
    }
}
