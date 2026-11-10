using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WorkTimeTracker.App.ViewModels;

/// <summary>
/// Отмена последнего действия вкладки (F5.3, F8.20, ADR-0004): уведомление «… · Отменить» на 8 секунд
/// и команда для Ctrl+Z. У каждой вкладки свой экземпляр — отменяется последнее действие активной вкладки.
/// Отмена остаётся доступной и после скрытия уведомления — до следующего изменения (<see cref="Invalidate"/>).
/// </summary>
public sealed partial class UndoToastViewModel : ObservableObject
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);

    private readonly TimeProvider _time;
    private Func<Task>? _undo;
    private int _version;

    public UndoToastViewModel(TimeProvider time)
    {
        _time = time;
        Text = "";
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; set; }

    /// <summary>Предлагает отменить только что выполненное действие (заменяет предыдущее предложение).</summary>
    public void Offer(string text, Func<Task> undo)
    {
        _undo = undo;
        Text = text;
        CanUndo = true;
        IsVisible = true;
        _ = HideLaterAsync(++_version);
    }

    /// <summary>После другого изменения отмена недоступна.</summary>
    public void Invalidate()
    {
        _undo = null;
        _version++;
        CanUndo = false;
        IsVisible = false;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        var undo = _undo;
        Invalidate();
        if (undo is not null)
        {
            await undo();
        }
    }

    private async Task HideLaterAsync(int version)
    {
        await Task.Delay(Duration, _time);
        if (version == _version)
        {
            IsVisible = false;
        }
    }
}
