namespace WorkTimeTracker.App.Services;

/// <summary>Шина событий приложения: изменение записей нужно отразить и в журнале, и в статистике.</summary>
public sealed class AppEvents
{
    public event EventHandler? EntriesChanged;

    public void RaiseEntriesChanged(object? sender) => EntriesChanged?.Invoke(sender, EventArgs.Empty);
}
