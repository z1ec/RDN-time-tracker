using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.App.ViewModels;
using WorkTimeTracker.Core.Abstractions;
using WorkTimeTracker.Core.Services;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.App.Services;

/// <summary>Композиция зависимостей (DI). Один корень для UI, self-test и тестов.</summary>
public static class AppComposition
{
    /// <summary>Слой данных и доменные сервисы — без UI (используется и в <c>--self-test</c>).</summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services, DataPaths paths, TimeProvider time)
    {
        services.AddSingleton(paths);
        services.AddSingleton(time);
        services.AddSingleton(_ => new SqliteConnectionFactory(paths.DatabasePath));
        services.AddSingleton<Migrator>();
        services.AddSingleton<EntryRepository>();
        services.AddSingleton<IEntryRepository>(sp => sp.GetRequiredService<EntryRepository>());
        services.AddSingleton<NotepadRepository>();
        services.AddSingleton<INotepadRepository>(sp => sp.GetRequiredService<NotepadRepository>());
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsStore>());
        services.AddSingleton<BackupService>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<EntryService>();
        services.AddSingleton<NotepadService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AppEvents>();
        return services;
    }

    /// <summary>ViewModel-и и UI-сервисы.</summary>
    public static IServiceCollection AddUiServices(this IServiceCollection services, IUserInteraction interaction)
    {
        services.AddSingleton(interaction);
        services.AddSingleton<ThemeService>();
        services.AddSingleton<JournalViewModel>();
        services.AddSingleton<NotepadViewModel>();
        services.AddSingleton<StatisticsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }

    public static ServiceProvider Build(DataPaths paths, TimeProvider time, IUserInteraction interaction) =>
        new ServiceCollection()
            .AddCoreServices(paths, time)
            .AddUiServices(interaction)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
