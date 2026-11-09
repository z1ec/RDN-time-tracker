# Архитектура

WorkTime Tracker — desktop-приложение на .NET 10 и Avalonia 12 с тремя слоями: **Core** (домен) ← **Data** (SQLite) ← **App** (UI, MVVM). Вся бизнес-логика — в Core и покрыта тестами; в code-behind — только визуальные вещи (фокус, маршрутизация клавиш).

## Слои и зависимости

```mermaid
flowchart LR
    subgraph App["WorkTimeTracker.App (Avalonia, MVVM)"]
        Views["Views (*.axaml)"] --> VMs["ViewModels"]
        VMs --> UISvc["Services: SettingsService, AppEvents,<br/>IUserInteraction, ThemeService, SelfTest"]
        Controls["Controls: BarChart, YearHeatmap"]
    end
    subgraph Data["WorkTimeTracker.Data"]
        Repo["EntryRepository / SettingsStore"] --> Factory["SqliteConnectionFactory"]
        Migrator --> Factory
        Backup["BackupService"] --> Factory
        Init["DatabaseInitializer"]
    end
    subgraph Core["WorkTimeTracker.Core (без зависимостей)"]
        Parser["QuickEntryParser"] --> Rules["EntryRules / EntryValidator"]
        Service["EntryService"] --> Parser
        Stats["StatisticsCalculator / PeriodNavigator"]
        Csv["CsvExporter"]
        Abstr["IEntryRepository / ISettingsStore"]
    end
    VMs --> Service
    VMs --> Stats
    VMs --> Csv
    Service --> Abstr
    Repo -. реализует .-> Abstr
    App --> Data
    Factory --> SQLite[("worktime.db")]
```

| Проект | Ответственность | Зависит от |
|---|---|---|
| `WorkTimeTracker.Core` | модели, разбор быстрого ввода, правила и пересечения, статистика, периоды, форматирование по-русски, CSV, интерфейсы хранилища, сценарии `EntryService` | — |
| `WorkTimeTracker.Data` | пути к данным, соединения, миграции, репозиторий, настройки, бэкапы, подготовка БД при запуске | Core, Microsoft.Data.Sqlite, Dapper |
| `WorkTimeTracker.App` | окно, экраны, ViewModel-и, свои графики, DI, логи, CLI-режимы `--version` / `--self-test` / `--data-dir` | Core, Data, Avalonia, CommunityToolkit.Mvvm, M.E.DependencyInjection, Serilog |
| `tools/IconGen` | генератор иконки (SkiaSharp) | SkiaSharp |

## Поток данных: быстрый ввод

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant V as JournalView
    participant VM as JournalViewModel
    participant S as EntryService (Core)
    participant P as QuickEntryParser
    participant R as EntryRepository (Data)
    participant E as AppEvents
    U->>V: печатает «0900 1800 созвон»
    V->>VM: QuickInput (привязка)
    VM->>P: ParseMany → предпросмотр «✓ 10 нояб. · 09:00 → 18:00 …»
    U->>V: Enter
    V->>VM: AddCommand
    VM->>S: AddFromTextAsync(text, выбранная дата)
    S->>P: ParseMany (те же правила)
    S->>R: GetRangeAsync(дата±1) → EntryValidator: пересечения
    S->>R: AddAsync (одна транзакция)
    R-->>S: записи с Id
    S-->>VM: EntryOperationResult
    VM->>VM: перезагрузка сводки и списка
    VM->>E: RaiseEntriesChanged
    E-->>VM: StatisticsViewModel пересчитывает статистику
```

## Ключевые классы

| Класс | Где | Что делает |
|---|---|---|
| `TimeRange`, `WorkEntry`, `EntryDraft` | Core/Model | модель времени: минуты от начала даты записи, ночные смены (ADR-0003) |
| `QuickEntryParser` | Core/Parsing | грамматика быстрого ввода, ошибки с фрагментом (`docs/INPUT_FORMAT.md`) |
| `EntryRules` | Core/Validation | единые правила записи для быстрого ввода, формы и редактирования |
| `EntryValidator` | Core/Validation | запрет пересечений в абсолютных минутах (ADR-0007) |
| `EntryService` | Core/Services | сценарии: добавить из текста/формы, изменить, удалить, отменить удаление |
| `PeriodNavigator`, `StatisticsCalculator` | Core/Statistics | периоды, ◀/▶, все показатели (`docs/STATISTICS.md`) |
| `RuFormat`, `DurationFormatter` | Core/Formatting | русские даты, «8 ч 05 мин», подписи периодов |
| `CsvExporter` | Core/Export | CSV для русского Excel |
| `DataPaths` | Data | папка данных: `--data-dir` → `portable.txt` → `%LOCALAPPDATA%` |
| `Migrator`, `DatabaseInitializer` | Data | миграции, автобэкап, очистка, понятные ошибки открытия БД |
| `EntryRepository`, `SettingsStore`, `BackupService` | Data | SQL через Dapper, настройки, копии через Backup API |
| `AppComposition` | App/Services | регистрация зависимостей; `AddCoreServices` используется и UI, и self-test |
| `MainWindowViewModel` | App/ViewModels | навигация и последовательность запуска |
| `JournalViewModel`, `StatisticsViewModel`, `SettingsViewModel`, `AboutViewModel` | App/ViewModels | экраны |
| `BarChart`, `YearHeatmap` | App/Controls | свои графики на `DrawingContext` (ADR-0005) |
| `SelfTest` | App/Services | режим `--self-test` для CI |

## Запуск приложения

1. `Program.Main`: культура `ru-RU` (`RuFormat.Culture`) для всего процесса; разбор аргументов. `--version` и `--self-test` выполняются без UI и завершают процесс.
2. `App.OnFrameworkInitializationCompleted`: `DataPaths` → Serilog (`logs\app-YYYYMMDD.log`) → глобальные обработчики ошибок → DI-контейнер → `MainWindow` с `MainWindowViewModel`.
3. `MainWindow.Opened` → `MainWindowViewModel.InitializeAsync`: `DatabaseInitializer` (папки → автобэкап → миграции → очистка удалённых старше 30 дней) → загрузка настроек → тема → загрузка журнала и статистики. Если БД не открылась — диалог с путём и «Открыть папку», затем выход с кодом 2; данные не трогаются (F4.7).
4. Закрытие окна сохраняет его размер и положение (F4.8).

## MVVM-соглашения

- **CommunityToolkit.Mvvm**, source generators: `[ObservableProperty] public partial T Prop { get; set; }` (partial-свойства, без инициализаторов — значения задаются в конструкторе), `[RelayCommand]` для команд, `NotifyCanExecuteChangedFor` для доступности кнопок.
- **Compiled bindings** включены для всего проекта (`AvaloniaUseCompiledBindingsByDefault`), у каждого представления и шаблона — `x:DataType`. Ошибка в привязке — ошибка компиляции.
- ViewModel-и не знают про окна: диалоги, выбор файла, открытие папок и ссылок — через `IUserInteraction` (в тестах — `FakeUserInteraction`).
- Связь между экранами — `AppEvents.EntriesChanged` и `SettingsService.Changed`; экраны перезагружают свои данные сами.
- Code-behind содержит только визуальное: фокус поля ввода, Enter/Shift+Enter/Esc в поле быстрого ввода, Ctrl+F (фокус поиска), Ctrl+Z (с учётом фокуса в текстовом поле), двойной клик по строке.
- Все интерактивные элементы имеют `AutomationProperties.Name` — для доступности и для поиска в headless-тестах.

## Зависимости и DI

`Microsoft.Extensions.DependencyInjection`, все сервисы и ViewModel-и — singleton (одно окно, один пользователь). `ValidateOnBuild = true` ловит ошибки регистрации при старте (и в self-test). `TimeProvider` регистрируется как зависимость: в приложении — `TimeProvider.System`, в тестах — `FakeTimeProvider`.

## Потоки и асинхронность

- `Microsoft.Data.Sqlite` выполняет запросы синхронно, поэтому `SqliteConnectionFactory.RunAsync` уносит каждую операцию в пул потоков; продолжения возвращаются в UI-поток через `SynchronizationContext` Avalonia. UI не блокируется.
- Повторные загрузки (быстрый ввод в поиск, переключение периодов) защищены счётчиком версии запроса: устаревший ответ отбрасывается.
- `SettingsService` сохраняет настройки строго по очереди (`SemaphoreSlim`) и всегда пишет самое свежее состояние.
- `ThemeService` меняет тему только из UI-потока (через `Dispatcher`).

## Обработка ошибок (F7.10)

| Источник | Что происходит |
|---|---|
| Ошибка ввода (парсер, правила, пересечение) | красный текст под полем или в диалоге; никаких модальных окон |
| БД не открывается | `DatabaseOpenException` → диалог с путём к файлу и «Открыть папку» → выход; файл не меняется |
| Ошибка ввода-вывода при экспорте/бэкапе | запись в лог + диалог «Не удалось …» |
| Необработанное исключение в UI | `Dispatcher.UIThread.UnhandledException`: лог + диалог «Что-то пошло не так» с кнопкой открытия папки логов; приложение продолжает работу |
| Исключение в фоновой задаче / домене | `TaskScheduler.UnobservedTaskException`, `AppDomain.UnhandledException` → лог |

Данные не теряются: каждое изменение уже записано в БД своей транзакцией.

## Дизайн

- **Тема:** Avalonia `FluentTheme` с палитрами `ColorPaletteResources`; акцентный цвет — **индиго** (`#4F46E5` в светлой теме, `#6366F1` в тёмной). По умолчанию тема следует системной; выбор — в настройках.
- **Шрифт:** Inter (`Avalonia.Fonts.Inter`). Размеры: заголовок экрана 24 SemiBold, заголовок блока 15 SemiBold, KPI 28 SemiBold, основной текст 14, вспомогательный 12.
- **Сетка и формы:** отступы кратны 8 (страница 24, карточки 16, промежутки 8/12/16), скругления карточек 12, контролов 8. Блоки — карточки (`Border.card`) на чуть более тёмном фоне страницы.
- **Цветовые токены** (`Styles/Theme.axaml`, светлая / тёмная): `PageBackground` `#F5F6FA` / `#131418`, `CardBackground` `#FFFFFF` / `#1E2028`, `CardBorder` `#E3E6EF` / `#2D303C`, `SubtleText` `#6B7280` / `#9CA3AF`, `AccentText` `#4338CA` / `#A5B4FC`, `Positive` `#047857` / `#34D399`, `Negative` (недоработка) `#C2410C` / `#FB923C`, `Error` `#DC2626` / `#F87171`, графики — оттенки индиго, линия нормы — оранжевая.
- **Иконки:** векторные `PathIcon` с геометрией Fluent UI System Icons (MIT), `Styles/Icons.axaml`. Эмодзи и растровые картинки в интерфейсе не используются (кроме иконки приложения).
- **Состояния:** пустой журнал — «Пока нет записей — введите, например, 0900 1800 планёрка»; поиск без результатов — «Ничего не найдено…»; пустой период статистики — подпись под KPI; ошибки ввода — красным под полем; удаление — тёмное уведомление внизу списка с кнопкой «Отменить».
- **Окно:** стартовый размер 1200×780, минимальный 900×600; боковая панель навигации в компактном режиме (иконки, подписи — по кнопке меню).
- Вид проверяется headless-скриншотами в обеих темах (`scripts/screenshots.sh`, `docs/images/`).
