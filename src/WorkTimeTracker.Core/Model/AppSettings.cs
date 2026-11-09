namespace WorkTimeTracker.Core.Model;

/// <summary>Тема оформления (F7.8).</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Положение и размер окна (F4.8).</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

/// <summary>Пользовательские настройки (F7.4).</summary>
public sealed record AppSettings
{
    public static readonly IReadOnlySet<DayOfWeek> DefaultWorkDays = new HashSet<DayOfWeek>
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
    };

    /// <summary>Дневная норма в минутах (по умолчанию 8:00).</summary>
    public int DailyNormMinutes { get; init; } = 480;

    /// <summary>Рабочие дни недели для расчёта нормы (по умолчанию Пн–Пт).</summary>
    public IReadOnlySet<DayOfWeek> WorkDays { get; init; } = DefaultWorkDays;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public WindowPlacement? Window { get; init; }
}
