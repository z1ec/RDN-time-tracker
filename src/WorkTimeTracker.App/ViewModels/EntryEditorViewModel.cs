using CommunityToolkit.Mvvm.ComponentModel;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Диалог редактирования записи (F6.3) и строки блокнота (F9.5). Валидация — та же, что при добавлении
/// (<see cref="EntryService.BuildDraft"/>); как сохранять, задаёт владелец: «Журнал» — изменить запись
/// (пересечения без учёта самой записи — ошибка), «Блокнот» — изменить строку по правилам F9.5.
/// </summary>
public sealed partial class EntryEditorViewModel : ObservableObject
{
    private readonly EntryService _entries;
    private readonly Func<EntryDraft, Task<IReadOnlyList<string>>> _save;

    /// <summary>Правка записи журнала (F6.3).</summary>
    public EntryEditorViewModel(WorkEntry entry, EntryService entries)
        : this(entry, entries, async draft => (await entries.UpdateAsync(entry.Id, draft)).Errors, "Изменить запись", "")
    {
    }

    /// <param name="entry">Исходные значения.</param>
    /// <param name="entries">Сервис записей: разбор, правила, «сегодня».</param>
    /// <param name="save">Сохранение; возвращает ошибки (пусто — успех).</param>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="hint">Пояснение под заголовком (пусто — нет).</param>
    public EntryEditorViewModel(WorkEntry entry, EntryService entries, Func<EntryDraft, Task<IReadOnlyList<string>>> save, string title, string hint)
    {
        _entries = entries;
        _save = save;
        Entry = entry;
        Title = title;
        Hint = hint;
        DurationText = "";
        Comment = entry.Comment;
        Date = entry.Date.ToDateTime(TimeOnly.MinValue);
        Start = TimeSpan.FromMinutes(entry.StartMinute);
        End = TimeSpan.FromMinutes(entry.EndMinute % TimeRange.MinutesPerDay);
        Validate();
    }

    public WorkEntry Entry { get; }

    public string Title { get; }

    public string Hint { get; }

    public bool HasHint => Hint.Length > 0;

    public DateTimeOffset MaxDate => _entries.Today.ToDateTime(TimeOnly.MinValue);

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    public partial TimeSpan? Start { get; set; }

    [ObservableProperty]
    public partial TimeSpan? End { get; set; }

    [ObservableProperty]
    public partial string Comment { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string DurationText { get; set; }

    [ObservableProperty]
    public partial bool CanSave { get; set; }

    public string CommentCounter => $"{Comment?.Length ?? 0}/{EntryRules.MaxCommentLength}";

    partial void OnDateChanged(DateTime? value) => Validate();

    partial void OnStartChanged(TimeSpan? value) => Validate();

    partial void OnEndChanged(TimeSpan? value) => Validate();

    partial void OnCommentChanged(string value)
    {
        OnPropertyChanged(nameof(CommentCounter));
        Validate();
    }

    /// <summary>Сохраняет изменения. Возвращает <c>true</c> при успехе; иначе текст ошибки — в <see cref="Error"/>.</summary>
    public async Task<bool> SaveAsync()
    {
        var draft = BuildDraft();
        if (!draft.IsSuccess)
        {
            Error = draft.Error;
            return false;
        }

        var errors = await _save(draft.Value!);
        if (errors.Count > 0)
        {
            Error = string.Join(Environment.NewLine, errors);
            return false;
        }

        return true;
    }

    private Result<EntryDraft> BuildDraft()
    {
        if (Date is null || Start is null || End is null)
        {
            return Result<EntryDraft>.Fail("Укажите дату, начало и конец");
        }

        return _entries.BuildDraft(DateOnly.FromDateTime(Date.Value), Start.Value, End.Value, Comment);
    }

    private void Validate()
    {
        var draft = BuildDraft();
        CanSave = draft.IsSuccess;
        Error = draft.IsSuccess ? null : draft.Error;
        DurationText = draft.IsSuccess ? FormHelpers.DurationWithNextDay(draft.Value!.Range) : "";
    }
}

/// <summary>Общие подписи для формы и диалога.</summary>
public static class FormHelpers
{
    /// <summary>«9 ч 00 мин» или «4 ч 00 мин · +1 день» для ночной смены (F2.4).</summary>
    public static string DurationWithNextDay(TimeRange range) =>
        range.EndsNextDay ? $"{DurationFormatter.Format(range.DurationMinutes)} · +1 день" : DurationFormatter.Format(range.DurationMinutes);
}
