using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WorkTimeTracker.App.Tests.TestAppBuilder))]

namespace WorkTimeTracker.App.Tests;

/// <summary>Настройка headless-платформы Avalonia для UI-тестов.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
