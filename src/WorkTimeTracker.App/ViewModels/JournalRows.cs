using CommunityToolkit.Mvvm.ComponentModel;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>Строка плоского списка журнала: заголовок дня или запись (F7.2).</summary>
public abstract class JournalRow : ObservableObject
{
    /// <summary>Можно ли выделить строку (заголовки дней — нельзя).</summary>
    public abstract bool IsSelectable { get; }
}

/// <summary>Заголовок группы: «Пн, 9 ноября · 8 ч 15 мин».</summary>
public sealed class DayHeaderRow(DateOnly date, string title, int totalMinutes, bool isToday) : JournalRow
{
    public DateOnly Date { get; } = date;

    public string Title { get; } = title;

    public string TotalText { get; } = DurationFormatter.Format(totalMinutes);

    public bool IsToday { get; } = isToday;

    public override bool IsSelectable => false;
}

/// <summary>Запись: «09:00 – 13:00 · 4 ч 00 мин · комментарий».</summary>
public sealed class EntryRow(WorkEntry entry) : JournalRow
{
    public WorkEntry Entry { get; } = entry;

    public long Id => Entry.Id;

    public string TimeText { get; } = $"{RuFormat.Clock(entry.StartMinute)} – {RuFormat.Clock(entry.EndMinute)}";

    /// <summary>Отметка ночной смены: конец на следующие сутки.</summary>
    public bool EndsNextDay => Entry.EndsNextDay;

    public string DurationText { get; } = DurationFormatter.Format(entry.DurationMinutes);

    public string Comment => Entry.Comment;

    public bool HasComment => Entry.Comment.Length > 0;

    public override bool IsSelectable => true;

    public string AccessibleName => HasComment ? $"{TimeText}, {DurationText}, {Comment}" : $"{TimeText}, {DurationText}";
}
