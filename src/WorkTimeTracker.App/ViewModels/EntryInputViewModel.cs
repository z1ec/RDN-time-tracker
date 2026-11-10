using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTimeTracker.Core.Formatting;
using WorkTimeTracker.Core.Model;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Core.Validation;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Ввод записей: быстрый ввод с предпросмотром (F1) и форма (F2). Один и тот же контрол в «Журнале» и
/// «Блокноте» (F8.2): разбор и проверки общие, отличается только способ сохранения, который задаёт владелец.
/// </summary>
public sealed partial class EntryInputViewModel : ObservableObject
{
    private readonly EntryService _entries;
    private readonly TimeProvider _time;
    private readonly Func<string, DateOnly, Task<EntryOperationResult>> _addText;
    private readonly Func<EntryDraft, Task<EntryOperationResult>> _addDraft;
    private readonly Func<int, Task> _added;

    /// <param name="entries">Сервис записей: разбор, правила, «сегодня».</param>
    /// <param name="time">Часы (кнопки «Сейчас»).</param>
    /// <param name="addText">Сохранение записей из текста быстрого ввода.</param>
    /// <param name="addDraft">Сохранение записи из формы.</param>
    /// <param name="added">Вызывается после успешного добавления; аргумент — число записей.</param>
    public EntryInputViewModel(
        EntryService entries,
        TimeProvider time,
        Func<string, DateOnly, Task<EntryOperationResult>> addText,
        Func<EntryDraft, Task<EntryOperationResult>> addDraft,
        Func<int, Task> added)
    {
        _entries = entries;
        _time = time;
        _addText = addText;
        _addDraft = addDraft;
        _added = added;

        QuickInput = "";
        PreviewText = "";
        FormComment = "";
        FormDurationText = "";
        SelectedDate = _entries.Today.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>«Добавлено: 2 записи» — текст для строки состояния владельца.</summary>
    public static string AddedText(int count) => $"Добавлено: {count} {RuFormat.Plural(count, "запись", "записи", "записей")}";

    /// <summary>Наступил новый день: обновить границу выбора даты.</summary>
    public void RefreshToday() => OnPropertyChanged(nameof(MaxDate));

    // ---------- Быстрый ввод (F1) ----------

    [ObservableProperty]
    public partial string QuickInput { get; set; }

    /// <summary>Дата для ввода без даты и для формы (по умолчанию — сегодня).</summary>
    [ObservableProperty]
    public partial DateTime? SelectedDate { get; set; }

    [ObservableProperty]
    public partial string PreviewText { get; set; }

    [ObservableProperty]
    public partial bool PreviewIsError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool CanAdd { get; set; }

    public bool HasPreview => PreviewText.Length > 0;

    /// <summary>Последний доступный для выбора день (будущие даты запрещены).</summary>
    public DateTime MaxDate => _entries.Today.ToDateTime(TimeOnly.MinValue);

    public DateOnly SelectedDay => SelectedDate is { } d ? DateOnly.FromDateTime(d) : _entries.Today;

    public string SelectedDateLabel => SelectedDay == _entries.Today ? "Сегодня" : RuFormat.DayMonthShort(SelectedDay);

    partial void OnQuickInputChanged(string value) => UpdatePreview();

    partial void OnPreviewTextChanged(string value) => OnPropertyChanged(nameof(HasPreview));

    partial void OnSelectedDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(SelectedDay));
        OnPropertyChanged(nameof(SelectedDateLabel));
        UpdatePreview();
        UpdateFormPreview();
    }

    /// <summary>Живой предпросмотр (F1.3): «✓ 10 нояб. · 09:00 → 18:00 · 9 ч 00 мин · «созвон»» или ошибка.</summary>
    public void UpdatePreview()
    {
        var batch = _entries.Parser.ParseMany(QuickInput, SelectedDay);
        if (batch.IsEmpty)
        {
            PreviewText = "";
            PreviewIsError = false;
            CanAdd = false;
            return;
        }

        if (!batch.IsSuccess)
        {
            PreviewText = "✗ " + string.Join(Environment.NewLine + "✗ ", batch.ErrorMessages);
            PreviewIsError = true;
            CanAdd = false;
            return;
        }

        PreviewIsError = false;
        CanAdd = true;
        if (batch.Lines.Count == 1)
        {
            var d = batch.Lines[0].Draft;
            var text = $"✓ {RuFormat.DayMonthShort(d.Date)} · {RuFormat.Clock(d.Range.StartMinute)} → {RuFormat.ClockWithDayOffset(d.Range.EndMinute)} · {DurationFormatter.Format(d.Range.DurationMinutes)}";
            PreviewText = d.Comment.Length > 0 ? $"{text} · «{d.Comment}»" : text;
        }
        else
        {
            var total = batch.Lines.Sum(l => l.Draft.Range.DurationMinutes);
            var n = batch.Lines.Count;
            PreviewText = $"✓ {n} {RuFormat.Plural(n, "запись", "записи", "записей")} · всего {DurationFormatter.Format(total)}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var result = await _addText(QuickInput, SelectedDay);
        if (!result.IsSuccess)
        {
            PreviewText = "✗ " + string.Join(Environment.NewLine + "✗ ", result.Errors);
            PreviewIsError = true;
            CanAdd = false;
            return;
        }

        QuickInput = "";
        await _added(result.Entries.Count);
    }

    /// <summary>Esc в поле ввода: очистить (F7.6).</summary>
    [RelayCommand]
    private void ClearInput()
    {
        QuickInput = "";
        UpdatePreview();
    }

    [RelayCommand]
    private void ResetDate() => SelectedDate = _entries.Today.ToDateTime(TimeOnly.MinValue);

    // ---------- Форма (F2) ----------

    [ObservableProperty]
    public partial bool IsFormExpanded { get; set; }

    [ObservableProperty]
    public partial TimeSpan? FormStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? FormEnd { get; set; }

    [ObservableProperty]
    public partial string FormComment { get; set; }

    [ObservableProperty]
    public partial string FormDurationText { get; set; }

    [ObservableProperty]
    public partial string? FormError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromFormCommand))]
    public partial bool CanAddFromForm { get; set; }

    public string FormCommentCounter => $"{FormComment.Length}/{EntryRules.MaxCommentLength}";

    partial void OnFormStartChanged(TimeSpan? value) => UpdateFormPreview();

    partial void OnFormEndChanged(TimeSpan? value) => UpdateFormPreview();

    partial void OnFormCommentChanged(string value)
    {
        OnPropertyChanged(nameof(FormCommentCounter));
        UpdateFormPreview();
    }

    /// <summary>Живой расчёт длительности формы и отметка «+1 день» (F2.4).</summary>
    public void UpdateFormPreview()
    {
        if (FormStart is null || FormEnd is null)
        {
            FormDurationText = "";
            FormError = null;
            CanAddFromForm = false;
            return;
        }

        var draft = _entries.BuildDraft(SelectedDay, FormStart.Value, FormEnd.Value, FormComment);
        FormError = draft.IsSuccess ? null : draft.Error;
        FormDurationText = draft.IsSuccess ? FormHelpers.DurationWithNextDay(draft.Value!.Range) : "";
        CanAddFromForm = draft.IsSuccess;
    }

    [RelayCommand]
    private void SetFormStartNow() => FormStart = NowTime();

    [RelayCommand]
    private void SetFormEndNow() => FormEnd = NowTime();

    [RelayCommand(CanExecute = nameof(CanAddFromForm))]
    private async Task AddFromFormAsync()
    {
        var draft = _entries.BuildDraft(SelectedDay, FormStart ?? TimeSpan.Zero, FormEnd ?? TimeSpan.Zero, FormComment);
        if (!draft.IsSuccess)
        {
            FormError = draft.Error;
            return;
        }

        var result = await _addDraft(draft.Value!);
        if (!result.IsSuccess)
        {
            FormError = string.Join(Environment.NewLine, result.Errors);
            CanAddFromForm = false;
            return;
        }

        FormStart = null;
        FormEnd = null;
        FormComment = "";
        await _added(1);
    }

    private TimeSpan NowTime()
    {
        var now = _time.GetLocalNow();
        return new TimeSpan(now.Hour, now.Minute, 0);
    }
}
