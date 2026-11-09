using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.App.Services;

/// <summary>
/// Текущие настройки в памяти + сохранение в БД при каждом изменении (F4.1, F7.4).
/// Сохранения выполняются строго по очереди и всегда пишут самое свежее состояние:
/// быстрые изменения подряд не могут перезаписать друг друга в обратном порядке.
/// </summary>
public sealed class SettingsService(ISettingsStore store)
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Current = await store.LoadAsync(ct).ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default)
    {
        var updated = change(Current);
        if (updated == Current)
        {
            return;
        }

        Current = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        await _saveLock.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            await store.SaveAsync(Current, ct).ConfigureAwait(true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
