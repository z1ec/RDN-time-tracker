using WorkTimeTracker.Core.Model;

namespace WorkTimeTracker.Core.Abstractions;

/// <summary>Хранилище настроек (таблица <c>settings</c>).</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
