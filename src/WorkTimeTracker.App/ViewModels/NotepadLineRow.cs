using CommunityToolkit.Mvvm.ComponentModel;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Строка блокнота: «10 нояб. · 09:00 – 13:00 · 4 ч 00 мин · комментарий» и статус (F9.3) —
/// метка («не в статистике», «в статистике: 09:00–17:00») и подсказка с причиной.
/// </summary>
public sealed class NotepadLineRow(NotepadLine line) : ObservableObject
{
    public NotepadLine Line { get; } = line;

    public long Id => Line.Id;

    /// <summary>«10 нояб.» — дата в каждой строке (F8.8).</summary>
    public string DateText { get; } = RuFormat.DayMonthShort(line.Date);

    public string TimeText { get; } = $"{RuFormat.Clock(line.StartMinute)} – {RuFormat.Clock(line.EndMinute)}";

    /// <summary>Отметка ночной смены: конец на следующие сутки.</summary>
    public bool EndsNextDay => Line.EndsNextDay;

    public string DurationText { get; } = DurationFormatter.Format(line.DurationMinutes);

    public string Comment => Line.Comment;

    public bool HasComment => Line.Comment.Length > 0;

    public NotepadLineStatus Status => Line.Status;

    public bool IsInStatistics => Status == NotepadLineStatus.InStatistics;

    /// <summary>«не в статистике»: строка приглушена (F9.3).</summary>
    public bool IsNotInStatistics => Status == NotepadLineStatus.NotInStatistics;

    /// <summary>«в статистике другое время» (F9.3, F9.5).</summary>
    public bool IsDifferentInStatistics => Status == NotepadLineStatus.DifferentInStatistics;

    /// <summary>Прозрачность содержимого строки: «не в статистике» — приглушена.</summary>
    public double ContentOpacity => IsNotInStatistics ? 0.55 : 1.0;

    /// <summary>Метка статуса; пусто для «в статистике».</summary>
    public string StatusLabel { get; } = NotepadFormat.StatusLabel(line);

    public bool HasStatusLabel => StatusLabel.Length > 0;

    /// <summary>Подсказка: с чем пересекается и что записано в статистику.</summary>
    public string StatusHint { get; } = NotepadFormat.StatusHint(line);

    public string AccessibleName
    {
        get
        {
            var text = HasComment ? $"{DateText}, {TimeText}, {DurationText}, {Comment}" : $"{DateText}, {TimeText}, {DurationText}";
            return HasStatusLabel ? $"{text}, {StatusLabel}" : text;
        }
    }
}
