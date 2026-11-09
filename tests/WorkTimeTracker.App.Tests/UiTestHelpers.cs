using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkTimeTracker.App.Views;

namespace WorkTimeTracker.App.Tests;

/// <summary>Помощники для headless UI-тестов.</summary>
public static class UiTestHelpers
{
    /// <summary>Открывает главное окно с ViewModel хоста и ждёт первой отрисовки.</summary>
    public static async Task<MainWindow> ShowMainWindowAsync(TestHost host, double width = 1200, double height = 780)
    {
        var window = new MainWindow { DataContext = host.Main, Width = width, Height = height };
        window.Show();
        await SettleAsync();
        return window;
    }

    /// <summary>Даёт отработать фоновым задачам (БД в пуле потоков) и очереди диспетчера.</summary>
    public static async Task SettleAsync(int rounds = 6)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Ждёт выполнения условия (с прокачкой диспетчера), не дольше таймаута.</summary>
    public static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Условие не выполнилось вовремя");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
    }

    public static T? FindDescendant<T>(this Control root, string? name = null)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => name is null || c.Name == name);

    public static T FindByAutomationName<T>(this Control root, string automationName)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == automationName);
}
