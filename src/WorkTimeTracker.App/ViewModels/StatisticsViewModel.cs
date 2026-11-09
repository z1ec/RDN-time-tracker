using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.App.Controls;
using WorkTimeTracker.App.Services;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Export;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Statistics;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>Тон KPI-карточки: подсветка переработки/недоработки и роста/падения.</summary>
public enum KpiTone
{
    Neutral,
    Positive,
    Negative,
}

/// <summary>KPI-карточка: заголовок, крупное значение, подпись.</summary>
public sealed record KpiCard(string Title, string Value, string Caption, KpiTone Tone = KpiTone.Neutral)
{
    public bool IsPositive => Tone == KpiTone.Positive;

    public bool IsNegative => Tone == KpiTone.Negative;
}

/// <summary>Экран «Статистика» (F3.1–F3.3, F3.5).</summary>
public sealed partial class StatisticsViewModel : ObservableObject
{
    /// <summary>Произвольный период длиннее этого показывается графиком по месяцам.</summary>
    public const int MaxDailyBars = 92;

    private static readonly PeriodKind[] KindOrder =
        [PeriodKind.Day, PeriodKind.Week, PeriodKind.Month, PeriodKind.Year, PeriodKind.AllTime, PeriodKind.Custom];

    private readonly EntryService _entries;
    private readonly IEntryRepository _repository;
    private readonly SettingsService _settings;
    private readonly IUserInteraction _interaction;
    private DateOnly? _firstDate;
    private int _loadVersion;
    private bool _suppressReload;
    private bool _active; // до первой явной загрузки (после инициализации БД) к БД не обращаемся

    public StatisticsViewModel(EntryService entries, IEntryRepository repository, SettingsService settings, AppEvents events, IUserInteraction interaction)
    {
        _entries = entries;
        _repository = repository;
        _settings = settings;
        _interaction = interaction;

        var today = _entries.Today;
        Period = PeriodNavigator.ForDate(PeriodKind.Week, today);
        PeriodLabel = RuFormat.PeriodLabel(Period);
        ChartTitle = "";
        StatusText = "";
        ChartItems = [];
        WeekdayItems = [];
        HeatmapCells = [];
        SelectedKindIndex = 1;
        CustomStart = today.AddDays(-13).ToDateTime(TimeOnly.MinValue);
        CustomEnd = today.ToDateTime(TimeOnly.MinValue);

        events.EntriesChanged += (_, _) => Reload();
        settings.Changed += (_, _) => Reload();
    }

    public IReadOnlyList<string> Kinds { get; } = ["День", "Неделя", "Месяц", "Год", "Всё время", "Период"];

    [ObservableProperty]
    public partial int SelectedKindIndex { get; set; }

    public PeriodKind SelectedKind => KindOrder[Math.Clamp(SelectedKindIndex, 0, KindOrder.Length - 1)];

    public bool IsCustom => SelectedKind == PeriodKind.Custom;

    [ObservableProperty]
    public partial DateTime? CustomStart { get; set; }

    [ObservableProperty]
    public partial DateTime? CustomEnd { get; set; }

    public DateTime MaxDate => _entries.Today.ToDateTime(TimeOnly.MinValue);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    public partial Period Period { get; set; }

    [ObservableProperty]
    public partial string PeriodLabel { get; set; }

    [ObservableProperty]
    public partial PeriodStatistics? Statistics { get; set; }

    public ObservableCollection<KpiCard> Kpis { get; } = [];

    [ObservableProperty]
    public partial string ChartTitle { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChartBar> ChartItems { get; set; }

    /// <summary>Линия дневной нормы на графике по дням (NaN — не рисовать).</summary>
    [ObservableProperty]
    public partial double ChartNorm { get; set; }

    [ObservableProperty]
    public partial bool HasChart { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChartBar> WeekdayItems { get; set; }

    [ObservableProperty]
    public partial bool HasWeekdays { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<HeatmapCell> HeatmapCells { get; set; }

    [ObservableProperty]
    public partial bool HasHeatmap { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    partial void OnSelectedKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(IsCustom));
        Period = MakePeriod(SelectedKind, _entries.Today);
        Reload();
    }

    partial void OnCustomStartChanged(DateTime? value) => OnCustomChanged();

    partial void OnCustomEndChanged(DateTime? value) => OnCustomChanged();

    private void OnCustomChanged()
    {
        if (IsCustom && CustomStart is not null && CustomEnd is not null)
        {
            Period = MakePeriod(PeriodKind.Custom, _entries.Today);
            Reload();
        }
    }

    private Period MakePeriod(PeriodKind kind, DateOnly anchor) => kind switch
    {
        PeriodKind.Custom => PeriodNavigator.Custom(
            CustomStart is { } s ? DateOnly.FromDateTime(s) : anchor,
            CustomEnd is { } e ? DateOnly.FromDateTime(e) : anchor),
        _ => PeriodNavigator.ForDate(kind, anchor, _firstDate),
    };

    private bool CanGoPrevious() => Period.Kind != PeriodKind.AllTime;

    private bool CanGoNext() => PeriodNavigator.CanGoNext(Period, _entries.Today);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Shift(-1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Shift(1);

    [RelayCommand]
    private void Today()
    {
        if (IsCustom)
        {
            var length = Period.DayCount;
            SetCustom(_entries.Today.AddDays(-(length - 1)), _entries.Today);
        }

        Period = MakePeriod(SelectedKind, _entries.Today);
        Reload();
    }

    private void Shift(int steps)
    {
        var shifted = PeriodNavigator.Shift(Period, steps);
        if (IsCustom)
        {
            SetCustom(shifted.Start, shifted.End);
        }

        Period = shifted;
        Reload();
    }

    private void SetCustom(DateOnly start, DateOnly end)
    {
        _suppressReload = true;
        CustomStart = start.ToDateTime(TimeOnly.MinValue);
        CustomEnd = end.ToDateTime(TimeOnly.MinValue);
        _suppressReload = false;
    }

    private void Reload()
    {
        if (_active && !_suppressReload)
        {
            _ = LoadAsync();
        }
    }

    /// <summary>Пересчитывает статистику выбранного периода.</summary>
    public async Task LoadAsync()
    {
        _active = true;
        var version = ++_loadVersion;
        var today = _entries.Today;
        _firstDate = await _repository.GetFirstDateAsync();
        if (Period.Kind == PeriodKind.AllTime)
        {
            Period = PeriodNavigator.ForDate(PeriodKind.AllTime, today, _firstDate);
        }

        var period = Period;
        var from = PeriodNavigator.Previous(period)?.Start ?? period.Start;
        var entries = await _repository.GetRangeAsync(from, period.End);
        if (version != _loadVersion)
        {
            return;
        }

        var stats = StatisticsCalculator.Calculate(period, entries, today, _settings.Current, _firstDate);
        Statistics = stats;
        PeriodLabel = RuFormat.PeriodLabel(period);
        IsEmpty = stats.EntryCount == 0;
        BuildKpis(stats);
        BuildCharts(stats, today);
        OnPropertyChanged(nameof(MaxDate));
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    private void BuildKpis(PeriodStatistics s)
    {
        Kpis.Clear();

        Kpis.Add(new KpiCard("Итого", DurationFormatter.FormatCompact(s.TotalMinutes), ComparisonCaption(s)));

        var balance = s.BalanceMinutes;
        var balanceCaption = s.NormDays == 0
            ? "норма не задана для этих дней"
            : $"норма {DurationFormatter.FormatShort(s.NormMinutes)} · {(balance >= 0 ? "переработка" : "недоработка")}";
        Kpis.Add(new KpiCard("Баланс с нормой", (balance > 0 ? "+" : "") + DurationFormatter.FormatCompact(balance), balanceCaption,
            s.NormDays == 0 ? KpiTone.Neutral : balance >= 0 ? KpiTone.Positive : KpiTone.Negative));

        Kpis.Add(new KpiCard("Рабочих дней", s.WorkDays.ToString(RuFormat.Culture),
            $"из {s.CalendarDays} {RuFormat.Plural(s.CalendarDays, "календарного", "календарных", "календарных")} · записей: {s.EntryCount}"));

        Kpis.Add(new KpiCard("В среднем за рабочий день", DurationFormatter.FormatCompact(Round(s.AveragePerWorkDay)),
            $"за календарный день: {DurationFormatter.Format(Round(s.AveragePerCalendarDay))}"));

        Kpis.Add(new KpiCard("Средняя запись", DurationFormatter.FormatCompact(Round(s.AverageEntryMinutes)),
            $"{s.EntryCount} {RuFormat.Plural(s.EntryCount, "запись", "записи", "записей")} за период"));

        Kpis.Add(new KpiCard("Начало и конец дня",
            s.AverageStartMinute is null ? "—" : $"{StatisticsCalculator.FormatAverageClock(s.AverageStartMinute)}–{StatisticsCalculator.FormatAverageClock(s.AverageEndMinute)}",
            "в среднем по первой и последней записи"));

        Kpis.Add(new KpiCard("Самый длинный день", s.LongestDay is { } l ? DurationFormatter.FormatCompact(l.Minutes) : "—",
            s.LongestDay is { } ld ? RuFormat.GroupHeader(ld.Date, _entries.Today) : "нет данных"));

        Kpis.Add(new KpiCard("Самый короткий день", s.ShortestDay is { } sh ? DurationFormatter.FormatCompact(sh.Minutes) : "—",
            s.ShortestDay is { } sd ? RuFormat.GroupHeader(sd.Date, _entries.Today) : "нет данных"));
    }

    private void BuildCharts(PeriodStatistics s, DateOnly today)
    {
        var kind = s.Period.Kind;
        var daily = kind is PeriodKind.Week or PeriodKind.Month || (kind == PeriodKind.Custom && s.Period.DayCount <= MaxDailyBars);
        var monthly = kind is PeriodKind.Year or PeriodKind.AllTime || (kind == PeriodKind.Custom && s.Period.DayCount > MaxDailyBars);

        if (daily)
        {
            var labelEvery = s.Daily.Count > 16 ? 2 : 1;
            ChartTitle = "По дням";
            ChartItems = [.. s.Daily.Select((d, i) => new ChartBar(
                kind == PeriodKind.Week ? $"{RuFormat.DayShort(d.Date.DayOfWeek)} {d.Date.Day}" : (i % labelEvery == 0 || d.Date == today ? d.Date.Day.ToString(RuFormat.Culture) : ""),
                d.Minutes,
                $"{RuFormat.GroupHeader(d.Date, today)}: {DurationFormatter.Format(d.Minutes)}",
                d.Date == today,
                d.Date > today || !_settings.Current.WorkDays.Contains(d.Date.DayOfWeek)))];
            ChartNorm = _settings.Current.DailyNormMinutes;
        }
        else if (monthly)
        {
            var multiYear = s.Monthly.Select(m => m.Year).Distinct().Count() > 1;
            ChartTitle = "По месяцам";
            ChartItems = [.. s.Monthly.Select(m => new ChartBar(
                multiYear ? $"{RuFormat.MonthShortNominative(m.Month)} {m.Year % 100:00}" : RuFormat.MonthShortNominative(m.Month),
                m.Minutes,
                $"{RuFormat.MonthNominative(m.Month)} {m.Year}: {DurationFormatter.Format(m.Minutes)}",
                m.Year == today.Year && m.Month == today.Month,
                new DateOnly(m.Year, m.Month, 1) > today))];
            ChartNorm = double.NaN;
        }
        else
        {
            ChartTitle = "";
            ChartItems = [];
            ChartNorm = double.NaN;
        }

        HasChart = ChartItems.Count > 0;

        HasWeekdays = kind != PeriodKind.Day && s.CalendarDays >= 7;
        WeekdayItems = HasWeekdays
            ? [.. s.Weekdays.Select(w => new ChartBar(
                RuFormat.DayShort(w.Day),
                w.AverageMinutes,
                $"{RuFormat.Capitalize(RuFormat.DayFull(w.Day))}: в среднем {DurationFormatter.Format(Round(w.AverageMinutes))} ({w.Occurrences} {RuFormat.Plural(w.Occurrences, "день", "дня", "дней")})",
                false,
                !_settings.Current.WorkDays.Contains(w.Day)))]
            : [];

        HasHeatmap = kind == PeriodKind.Year;
        HeatmapCells = HasHeatmap ? HeatmapCell.Build(s.Daily, today) : [];
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var p = Period;
        var name = p.Kind switch
        {
            PeriodKind.Month => $"worktime-{p.Start:yyyy-MM}.csv",
            PeriodKind.Year => $"worktime-{p.Start:yyyy}.csv",
            PeriodKind.Day => $"worktime-{p.Start:yyyy-MM-dd}.csv",
            _ => $"worktime-{p.Start:yyyy-MM-dd}_{p.End:yyyy-MM-dd}.csv",
        };
        var path = await _interaction.PickSaveFileAsync("Экспорт периода в CSV", name);
        if (path is null)
        {
            return;
        }

        try
        {
            var entries = await _repository.GetRangeAsync(p.Start, p.End);
            await File.WriteAllBytesAsync(path, CsvExporter.ToBytes(entries));
            StatusText = $"Сохранено: {Path.GetFileName(path)} ({entries.Count} {RuFormat.Plural(entries.Count, "запись", "записи", "записей")})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Экспорт CSV не удался");
            await _interaction.ShowErrorAsync("Не удалось сохранить файл", ex.Message);
        }
    }

    /// <summary>«▲ +2 ч 15 мин (+7%) к прошлой неделе, за те же 4 дня».</summary>
    public static string ComparisonCaption(PeriodStatistics s)
    {
        if (s.Comparison is not { } c)
        {
            return $"{s.EntryCount} {RuFormat.Plural(s.EntryCount, "запись", "записи", "записей")} за всё время";
        }

        var percent = c.DeltaPercent is { } p ? $" ({(p > 0 ? "+" : p < 0 ? "−" : "")}{Math.Abs(p):0}%)" : "";
        var partial = c.IsPartial ? $", за те же {c.ComparedDays} {RuFormat.Plural(c.ComparedDays, "день", "дня", "дней")}" : "";
        return $"{Arrow(c.DeltaMinutes)} {DurationFormatter.FormatSigned(c.DeltaMinutes)}{percent} {PreviousName(s.Period.Kind)}{partial}";
    }

    private static int Round(double minutes) => (int)Math.Round(minutes, MidpointRounding.AwayFromZero);

    private static string Arrow(int delta) => delta > 0 ? "▲" : delta < 0 ? "▼" : "=";

    private static string PreviousName(PeriodKind kind) => kind switch
    {
        PeriodKind.Day => "к прошлому дню",
        PeriodKind.Week => "к прошлой неделе",
        PeriodKind.Month => "к прошлому месяцу",
        PeriodKind.Year => "к прошлому году",
        _ => "к предыдущему периоду",
    };
}
