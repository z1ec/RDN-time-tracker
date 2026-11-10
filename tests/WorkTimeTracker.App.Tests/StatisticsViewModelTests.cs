using System.Text;
using Avalonia.Headless.XUnit;
using WorkTimeTracker.App.Controls;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Core.Model;
using static WorkTimeTracker.App.Tests.JournalViewModelTests;

namespace WorkTimeTracker.App.Tests;

/// <summary>ViewModel «Статистики»: F3.1–F3.3, F3.5. «Сегодня» — вторник 10.11.2026.</summary>
public class StatisticsViewModelTests
{
    private static async Task<TestHost> WithDataAsync()
    {
        var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = string.Join('\n',
            "02.11 0900 1300 планёрка", "02.11 1400 1800", "03.11 1000 1900", "06.11 2200 0200 релиз",
            "09.11 0900 1800", "0900 1200 сегодня", "27.10 0900 1700");
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        await host.Statistics.LoadAsync();
        return host;
    }

    [AvaloniaFact]
    public async Task DefaultWeek_KpisAndDailyChart()
    {
        using var host = await WithDataAsync();
        var vm = host.Statistics;

        Assert.Equal(PeriodKind.Week, vm.Period.Kind);
        Assert.Equal("Неделя 9–15 ноября 2026", vm.PeriodLabel);
        Assert.Equal(8, vm.Kpis.Count);
        Assert.Equal(("Итого", "12:00"), (vm.Kpis[0].Title, vm.Kpis[0].Value));
        Assert.Equal("▼ −5 ч 00 мин (−29%) к прошлой неделе, за те же 2 дня", vm.Kpis[0].Caption);
        Assert.Equal(("Баланс с нормой", "−4:00"), (vm.Kpis[1].Title, vm.Kpis[1].Value));
        Assert.True(vm.Kpis[1].IsNegative);
        Assert.Equal("норма 16 ч · недоработка", vm.Kpis[1].Caption);
        Assert.Equal("2", vm.Kpis[2].Value);
        Assert.Equal("из 2 календарных · записей: 2", vm.Kpis[2].Caption);
        Assert.Equal("09:00–15:00", vm.Kpis[5].Value);

        Assert.True(vm.HasChart);
        Assert.Equal("По дням", vm.ChartTitle);
        Assert.Equal(7, vm.ChartItems.Count);
        Assert.Equal("Пн 9", vm.ChartItems[0].Label);
        Assert.Equal("Пн, 9 ноября: 9 ч 00 мин", vm.ChartItems[0].Tooltip);
        Assert.True(vm.ChartItems[1].IsHighlighted);   // сегодня
        Assert.True(vm.ChartItems[5].IsMuted);         // суббота
        Assert.Equal(480, vm.ChartNorm);
        Assert.False(vm.HasWeekdays);                  // прошло меньше недели
        Assert.False(vm.HasHeatmap);
        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.True(vm.PreviousCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Navigate_PreviousWeek_AndToday()
    {
        using var host = await WithDataAsync();
        var vm = host.Statistics;
        vm.PreviousCommand.Execute(null);
        await WaitAsync(() => vm.PeriodLabel == "Неделя 2–8 ноября 2026" && vm.Kpis[0].Value == "21:00");
        Assert.True(vm.NextCommand.CanExecute(null));
        Assert.True(vm.HasWeekdays);
        Assert.Equal(7, vm.WeekdayItems.Count);
        Assert.Equal("Пн", vm.WeekdayItems[0].Label);
        Assert.StartsWith("Понедельник: в среднем 8 ч 00 мин", vm.WeekdayItems[0].Tooltip, StringComparison.Ordinal);
        Assert.Equal("▲ +13 ч 00 мин (+163%) к прошлой неделе", vm.Kpis[0].Caption);

        vm.NextCommand.Execute(null);
        await WaitAsync(() => vm.PeriodLabel == "Неделя 9–15 ноября 2026");
        vm.PreviousCommand.Execute(null);
        vm.TodayCommand.Execute(null);
        await WaitAsync(() => vm.PeriodLabel == "Неделя 9–15 ноября 2026");
    }

    [AvaloniaFact]
    public async Task Kinds_Day_Month_Year_AllTime()
    {
        using var host = await WithDataAsync();
        var vm = host.Statistics;

        vm.SelectedKindIndex = 0;
        await WaitAsync(() => vm.Period.Kind == PeriodKind.Day && vm.Kpis[0].Value == "3:00");
        Assert.Equal("Вторник, 10 ноября 2026", vm.PeriodLabel);
        Assert.False(vm.HasChart);

        vm.SelectedKindIndex = 2;
        await WaitAsync(() => vm.PeriodLabel == "Ноябрь 2026" && vm.ChartItems.Count == 30);
        Assert.Equal("1", vm.ChartItems[0].Label);
        Assert.Equal("", vm.ChartItems[1].Label);       // подписи через одну
        Assert.Equal("10", vm.ChartItems[9].Label);     // сегодня подписан всегда

        vm.SelectedKindIndex = 3;
        await WaitAsync(() => vm.PeriodLabel == "2026 год" && vm.HasHeatmap);
        Assert.Equal("По месяцам", vm.ChartTitle);
        Assert.Equal(12, vm.ChartItems.Count);
        Assert.Equal("ноя", vm.ChartItems[10].Label);
        Assert.True(vm.ChartItems[11].IsMuted);         // декабрь ещё впереди
        Assert.True(double.IsNaN(vm.ChartNorm));
        Assert.Equal(365, vm.HeatmapCells.Count);
        Assert.Equal(11, vm.Statistics!.NormDays);      // норма — с первой записи 27.10

        vm.SelectedKindIndex = 4;
        await WaitAsync(() => vm.Period.Kind == PeriodKind.AllTime && vm.ChartItems.Count == 2);
        Assert.Equal("Всё время · 27 октября – 10 ноября 2026", vm.PeriodLabel);
        Assert.False(vm.PreviousCommand.CanExecute(null));
        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.Equal("7 записей за всё время", vm.Kpis[0].Caption);
    }

    [AvaloniaFact]
    public async Task CustomPeriod_ShiftsBySameLength()
    {
        using var host = await WithDataAsync();
        var vm = host.Statistics;
        vm.SelectedKindIndex = 5;
        Assert.True(vm.IsCustom);
        vm.CustomStart = new DateTime(2026, 11, 2);
        vm.CustomEnd = new DateTime(2026, 11, 8);
        await WaitAsync(() => vm.PeriodLabel == "2–8 ноября 2026" && vm.Kpis[0].Value == "21:00");
        Assert.Equal(7, vm.ChartItems.Count);

        vm.PreviousCommand.Execute(null);
        await WaitAsync(() => vm.PeriodLabel == "26 октября – 1 ноября 2026");
        Assert.Equal(new DateTime(2026, 10, 26), vm.CustomStart);

        vm.TodayCommand.Execute(null);
        await WaitAsync(() => vm.PeriodLabel == "4–10 ноября 2026");
    }

    [AvaloniaFact]
    public async Task LongCustomPeriod_UsesMonthlyChart()
    {
        using var host = await WithDataAsync();
        var vm = host.Statistics;
        vm.SelectedKindIndex = 5;
        vm.CustomStart = new DateTime(2025, 11, 1);
        vm.CustomEnd = new DateTime(2026, 11, 10);
        await WaitAsync(() => vm.ChartTitle == "По месяцам");
        Assert.Equal("ноя 25", vm.ChartItems[0].Label);
    }

    [AvaloniaFact]
    public async Task ExportCsv_WritesPeriodEntries()
    {
        using var host = await WithDataAsync();
        var vm = host.Statistics;
        var path = Path.Combine(host.Dir, "export.csv");
        host.Interaction.SavePath = path;

        await vm.ExportCsvCommand.ExecuteAsync(null);

        var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.StartsWith("﻿Дата;Начало;Конец", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // заголовок + 2 записи недели
        Assert.Equal("Сохранено: export.csv (2 записи)", vm.StatusText);

        host.Interaction.SavePath = null; // отмена диалога
        await vm.ExportCsvCommand.ExecuteAsync(null);

        host.Interaction.SavePath = Path.Combine(host.Dir, "нет-такой-папки", "x.csv");
        await vm.ExportCsvCommand.ExecuteAsync(null);
        Assert.Equal("Не удалось сохранить файл", host.Interaction.Errors.Single().Title);
    }

    [AvaloniaFact]
    public async Task EmptyDatabase_ShowsEmptyState()
    {
        using var host = await TestHost.StartAsync();
        Assert.True(host.Statistics.IsEmpty);
        Assert.Equal("—", host.Statistics.Kpis[5].Value);
        Assert.Equal("нет данных", host.Statistics.Kpis[6].Caption);
    }

    [AvaloniaFact]
    public async Task NewEntries_RefreshStatistics()
    {
        using var host = await TestHost.StartAsync();
        host.Journal.Input.QuickInput = "0900 1000";
        await host.Journal.Input.AddCommand.ExecuteAsync(null);
        await WaitAsync(() => host.Statistics.Kpis[0].Value == "1:00");
    }

    [Fact]
    public void ChartScale_PicksNiceSteps()
    {
        Assert.Equal((60, 30), BarChart.Scale(0));
        Assert.Equal((720, 180), BarChart.Scale(545));
        Assert.Equal((720, 180), BarChart.Scale(700));
        Assert.Equal((12000, 3000), BarChart.Scale(9600));
    }

    [Fact]
    public void Heatmap_Levels()
    {
        var days = Enumerable.Range(0, 14).Select(i => new Core.Statistics.DayTotal(new DateOnly(2026, 1, 1).AddDays(i), i * 60)).ToList();
        var cells = HeatmapCell.Build(days, new DateOnly(2026, 1, 10));
        Assert.Equal(0, cells[0].Level);
        Assert.Equal(4, cells[13].Level);
        Assert.Equal(3, cells[0].Row);       // 01.01.2026 — четверг
        Assert.Equal(1, cells[4].Column);    // 05.01 — понедельник следующей недели
        Assert.True(cells[13].IsFuture);
        Assert.Empty(HeatmapCell.Build([], new DateOnly(2026, 1, 1)));
    }
}
