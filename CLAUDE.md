# CLAUDE.md — WorkTime Tracker

Трекер рабочего времени для Windows на C# / .NET 10 / Avalonia 12. Пользователь вводит периоды
строкой (`0900 1800 созвон`) или через форму, смотрит статистику (норма, сравнение, графики);
данные — в локальной SQLite. Разработка и тесты — на macOS/Linux, Windows-exe собирает GitHub Actions.
Репозиторий: https://github.com/z1ec/RDN-time-tracker (папка — клон существующего репо владельца).

Где подробности: `docs/README.md` — карта документации; задание — `docs/ORIGINAL_PROMPT.md`;
требования → код → тесты — `docs/FEATURES.md`; прогресс — `docs/PROGRESS.md`; история — `docs/DEVELOPMENT_LOG.md`.

## Команды

.NET 10 SDK на машине разработки может лежать в `~/.dotnet` → `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.

| Что | Команда |
|---|---|
| Сборка | `scripts/build.sh` (restore `--locked-mode` + `build -c Release`, предупреждения = ошибки) |
| Тесты + покрытие | `scripts/test.sh` (без категории Screenshots; порог покрытия Core 90%) |
| Запуск | `scripts/run.sh [--data-dir /tmp/wtt]` |
| Скриншоты | `scripts/screenshots.sh` → `docs/images/*.png` (обязательно открыть и посмотреть) |
| Windows-exe | `scripts/publish-win.sh [версия]` → `artifacts/win-x64/WorkTimeTracker.exe` |
| Упаковка/заметки релиза | `scripts/package-release.sh vX.Y.Z`, `scripts/release-notes.sh X.Y.Z` |
| Формат | `dotnet format`; проверка `dotnet format --verify-no-changes` |
| Self-test | `dotnet run --project src/WorkTimeTracker.App -- --self-test --data-dir /tmp/wtt-st` |

## Карта кода

- `src/WorkTimeTracker.Core` — домен без зависимостей:
  - `Model/` — `TimeRange` (минуты), `WorkEntry`, `EntryDraft`, `Period`, `AppSettings`;
  - `Parsing/QuickEntryParser` — грамматика быстрого ввода (`docs/INPUT_FORMAT.md`);
  - `Validation/EntryRules` (единые правила), `EntryValidator` (пересечения);
  - `Statistics/` — `PeriodNavigator`, `StatisticsCalculator` (`docs/STATISTICS.md`);
  - `Formatting/` — `RuFormat`, `DurationFormatter`; `Export/CsvExporter`;
  - `Services/EntryService` — сценарии добавления/изменения/удаления поверх `IEntryRepository`.
- `src/WorkTimeTracker.Data` — `DataPaths`, `SqliteConnectionFactory`, `Migrator` + `Migrations/*.sql`,
  `EntryRepository`, `SettingsStore`, `BackupService`, `DatabaseInitializer` (`docs/DATA_MODEL.md`).
- `src/WorkTimeTracker.App` — `Program.cs` (культура, CLI), `App.axaml.cs` (запуск, логи, ошибки),
  `Services/AppComposition` (DI; `AddCoreServices` общий с self-test), `ViewModels/`, `Views/`,
  `Controls/` (`BarChart`, `YearHeatmap`), `Styles/` (`Theme.axaml` — цвета, `Icons.axaml`, `AppStyles.axaml`).
- `tests/` — Core.Tests (парсер, статистика…), Data.Tests (временные БД), App.Tests (`TestHost` —
  приложение без окна с `FakeTimeProvider`; ViewModel-тесты, headless smoke, `ScreenshotTests`, `DemoData`).
- `tools/IconGen` — генерирует `App/Assets/app.ico` и `icon-256.png`.

## Инварианты и подводные камни

- **Модель времени:** `work_date` = дата начала, `start_min` 0…1439, `end_min` > `start_min`, ≤ +1440;
  `end_min > 1440` — следующие сутки, `1440` — ровно 24:00. Длительность = `end_min − start_min`.
- **Ночная смена целиком относится к дате начала** — в журнале, статистике и CSV.
- **Пересечения** — только через `EntryValidator` в абсолютных минутах (соседние дни ±1); касание допустимо.
- **`TimeProvider` везде** вместо `DateTime.Now`/`Today`; «сегодня» — `EntryService.Today`.
- **Мягкое удаление:** каждая выборка из `entries` — с `deleted_at IS NULL`. Отмена — только последнего удаления, пока не было других изменений.
- **Миграции только добавляются** (`NNNN_*.sql`, подряд с 1); существующие не редактировать. БД никогда не удалять и не пересоздавать автоматически.
- **Кириллица:** `LOWER`/`LIKE` в SQLite — только ASCII → поиск фильтруется в C# (`RuFormat.Culture.CompareInfo`).
- **Культура:** отображение только через `RuFormat` (явные русские названия поверх ru-RU — одинаково на NLS/ICU).
- **Compiled bindings** по умолчанию: у каждого представления/шаблона `x:DataType`. В стилях текст — селектор
  `:is(TextBlock).class` (иначе не применится к `SelectableTextBlock`). Свойства VM — `[ObservableProperty] public partial`, **без инициализаторов** (значения — в конструкторе).
- ViewModel-и не трогают окна: диалоги/файлы/папки — через `IUserInteraction`. Тема — через `ThemeService` (сама уходит в UI-поток).
- До первой `LoadAsync` ViewModel-и не обращаются к БД (флаг `_active`): сеттеры в конструкторе вызывают перезагрузку.
- **Без trimming и AOT** (ADR-0006). `EnableSingleFileAnalyzer=false` и `RuntimeIdentifiers=win-x64` у src-проектов — иначе lock-файлы зависят от SDK.
- Версии пакетов — только в `Directory.Packages.props`; после изменения — `dotnet restore --force-evaluate`, закоммитить lock-файлы.
- xunit.v3 **3.2.2** — версия, которую требует `Avalonia.Headless.XUnit 12.1.5`; не обновлять отдельно.
- В Avalonia 12 `ListBox` не фокусируемый — фокус у `ListBoxItem`; `TextBox.Watermark` устарел → `PlaceholderText`.
- Скриншоты: тема применяется **после** `MainWindowViewModel.InitializeAsync` (он ставит тему из настроек).

## Правила работы

- Любое изменение → тесты → `CHANGELOG.md` `[Unreleased]` → `docs/FEATURES.md` → профильный `docs/*.md` →
  запись в `docs/DEVELOPMENT_LOG.md`. Значимое решение → новый ADR в `docs/adr/`.
- Изменения UI → `scripts/screenshots.sh` и посмотреть PNG в обеих темах.
- Коммиты — Conventional Commits на русском с ID требований: `feat(F1): живой предпросмотр быстрого ввода`.
- Перед коммитом: `scripts/build.sh`, `scripts/test.sh`, `dotnet format --verify-no-changes`. После push — CI зелёный
  (`gh run watch` / `gh run view --log-failed`).

## Выпуск версии

1. `CHANGELOG.md`: `[Unreleased]` → `[X.Y.Z] - дата`, новая пустая `[Unreleased]`; `<Version>` в `Directory.Build.props`.
2. Коммит, push, зелёный CI. 3. `git tag vX.Y.Z && git push origin vX.Y.Z`.
4. `release.yml` публикует exe, zip и `SHA256SUMS.txt`; проверить `gh release view vX.Y.Z`. Подробно — `docs/BUILD_AND_RELEASE.md`.
