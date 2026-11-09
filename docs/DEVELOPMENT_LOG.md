# Журнал разработки

Хронология создания проекта по этапам (§11 задания `docs/ORIGINAL_PROMPT.md`). Новые записи добавляются в конец.

## Этап 0 — Подготовка · 2026-11-09

**Сделано:**
- Проверено окружение: `git 2.50.1`, macOS 26 (arm64). Системно установлен только .NET SDK 9.0.305 → .NET 10 SDK **10.0.403** установлен без sudo в `~/.dotnet` через `dotnet-install.sh --channel 10.0`.
- Задание скопировано дословно в `docs/ORIGINAL_PROMPT.md`.
- Созданы `LICENSE` (MIT, автор — Vladimir, имя из профиля GitHub `z1ec`), `.gitignore` (шаблон `dotnet new gitignore` + `.DS_Store`, `artifacts/`, `*.db`, `TestResults/`, `coverage/`), `.gitattributes`, `.editorconfig`, `CHANGELOG.md`, `docs/PROGRESS.md`, этот журнал.
- Определены актуальные стабильные версии зависимостей (`dotnet package search --exact-match`, 2026-11-09):

| Пакет | Версия | Комментарий |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Headless.XUnit, Avalonia.Skia | 12.1.5 | |
| CommunityToolkit.Mvvm | 8.4.2 | |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | |
| Microsoft.Data.Sqlite | 10.0.14 | |
| Dapper | 2.1.89 | |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | |
| Serilog / Serilog.Sinks.File | 4.4.0 / 7.0.0 | |
| xunit.v3 | **3.2.2** | не 4.0.3: `Avalonia.Headless.XUnit 12.1.5` зависит от `xunit.v3.extensibility.core 3.2.2` |
| xunit.runner.visualstudio | **3.1.5** | последняя ветка 3.x, совместимая с xunit.v3 3.x |
| Microsoft.NET.Test.Sdk | 18.11.0 | |
| coverlet.collector | 10.1.0 | |
| SkiaSharp (только `tools/IconGen`) | **3.119.4** | та же версия, что тянет `Avalonia.Skia 12.1.5`; 4.x — новая мажорная ветка, для утилиты иконки выигрыша нет |

- GitHub Actions (`api.github.com/repos/actions/<name>/releases/latest`): `checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7`.

**Решения:**
- **Репозиторий.** Папка проекта уже была клоном публичного репозитория `z1ec/RDN-time-tracker` с первым коммитом (`README.md`), созданного владельцем. Вместо создания нового `worktime-tracker` используется он: так не появляется лишний репозиторий, а владелец уже связал эту папку с ним. Все ссылки в документации ведут на `https://github.com/z1ec/RDN-time-tracker`.
- `git config user.name/user.email` уже заданы (`z1ec`), не менялись.

**Проблемы и как решены:**
- `gh` CLI отсутствует на машине → записано как внешняя блокировка в `docs/PROGRESS.md`. Push выполняется обычным `git push`, статус CI до установки `gh` проверяется через публичный REST API (репозиторий публичный).

**Проверка:** `~/.dotnet/dotnet --list-sdks` → `10.0.403`.

## Этап 1 — Скелет и CI с первого дня · 2026-11-09

**Сделано:**
- `WorkTimeTracker.slnx` (формат, который создаёт `dotnet new sln` в .NET 10): `src/` Core, Data, App; `tests/` три тестовых проекта на xUnit v3; `tools/IconGen`.
- `Directory.Build.props` (net10.0, `Nullable`, `TreatWarningsAsErrors`, `LangVersion=latest`, версия 1.0.0, lock-файлы, CPM), `Directory.Packages.props`, `global.json` (10.0.100, `latestFeature`).
- Окно Avalonia с `SplitView`-заглушкой; `Program.cs` с режимами `--version`, `--self-test` (минимальный: открыть SQLite и выполнить запрос), `--data-dir`. Для WinExe вывод в консоль через `AttachConsole`.
- Первые реальные классы с тестами: `DurationFormatter` (F7.7), `DataPaths` (F4.2, F4.3), `CommandLineOptions`; headless smoke-тест окна на настоящем Skia.
- `ci.yml`: job `test` (ubuntu: restore `--locked-mode` → `format --verify-no-changes` → build → тесты с покрытием и порогом Core ≥ 90% в `$GITHUB_STEP_SUMMARY`), job `windows` (publish → `--version` → self-test настоящего exe → артефакт).
- Скрипты `build.sh`, `test.sh`, `run.sh`, `publish-win.sh`, `coverage_summary.py`. ADR-0001, 0006, 0008, первая версия `CLAUDE.md`.

**Решения:**
- Нативные `.pdb` SkiaSharp/HarfBuzz исключаются из публикации MSBuild-таргетом — рядом с exe ничего лишнего (ADR-0006).
- Для LibraryImport (`AttachConsole`) включён `AllowUnsafeBlocks` в App.

**Проблемы и как решены:**
- **CI run #1 упал на `restore --locked-mode`.** Причина: публикация с `PublishSingleFile=true` включает анализатор single-file, а он добавляет в lock-файлы пакет `Microsoft.NET.ILLink.Tasks` с версией текущего SDK (10.0.14 у локального SDK 10.0.403); кроме того, `-r win-x64` менял набор RID у Core/Data. На CI с другим SDK lock-файлы не совпадали. Решение: `EnableSingleFileAnalyzer=false` в `Directory.Build.props`, `RuntimeIdentifiers=win-x64` у Core/Data; в CI добавлена проверка, что публикация не меняет lock-файлы.
- Локально проверено: `scripts/publish-win.sh` на macOS собирает `WorkTimeTracker.exe` (49 МБ) за ~5 с, lock-файлы после публикации не меняются.

**Проверка:** `dotnet test` — 25 passed; CI run #2 ✅ (тесты Linux + Windows publish + `--version` + self-test exe): https://github.com/z1ec/RDN-time-tracker/actions/runs/42608268503

**Коммиты:** `2045b51` build: скелет решения, CLI-режимы и CI с self-test Windows-exe; `474dc02` fix(build): lock-файлы не зависят от версии SDK и публикации

## Этап 2 — Домен (Core) · 2026-11-09

**Сделано:**
- Модели: `TimeRange` (value object, минуты от начала даты), `WorkEntry`, `EntryDraft`, `Period`/`PeriodKind`, `AppSettings`, `ThemePreference`, `WindowPlacement`.
- `QuickEntryParser` (F1.1–F1.4): ручной токенизатор + несколько `GeneratedRegex`; ошибки с позицией и фрагментом (`ParseError.DisplayText`), многострочный разбор `ParseMany` с префиксами «Строка N:».
- `EntryRules` — единая точка правил записи для быстрого ввода, формы и редактирования; `EntryValidator` — пересечения в абсолютных минутах (F7.1).
- `PeriodNavigator` (периоды, ◀/▶, предыдущий период, учитываемый конец), `StatisticsCalculator` + `PeriodStatistics` (все показатели F3.2, ряды для графиков по дням/месяцам/дням недели), `CsvExporter` (F3.5), `RuFormat` (русские даты и подписи периодов).
- Интерфейсы `IEntryRepository`, `ISettingsStore` и `EntryService` — сценарии добавления (текст/форма), редактирования, удаления и отмены поверх них.
- Документы `docs/INPUT_FORMAT.md`, `docs/STATISTICS.md`, ADR-0003, ADR-0007.

**Решения:**
- `RuFormat` задаёт названия месяцев и дней недели явно поверх `CultureInfo("ru-RU")`: на Windows с NLS и ICU, macOS и Linux сокращения различаются («нояб.» / «ноя»), а вывод и тесты должны быть одинаковыми.
- «Похоже на второй интервал» — 3–4 цифры, `H:mm` или «1–2 цифры + ещё время» в начале комментария; комментарий «3 задачи» допустим (см. INPUT_FORMAT, правило 9).
- `0900 0000` трактуется как конец в полночь (`end_min = 1440`), так форма с `TimePicker` (где нет «24:00») тоже может ввести такую запись.
- Сравнение с предыдущим периодом — с предыдущим периодом **целиком** (STATISTICS.md).
- Среднее по дням недели — по календарным дням (дни без записей в знаменателе).

**Проблемы и как решены:**
- Падение статического конструктора `RuFormat`: `Capitalize` обращался к ещё не созданной `Culture` → переход на `char.ToUpperInvariant`. Нашёлся тестами статистики.
- Две ошибки в ожиданиях собственных тестов (интервал «до» на самом деле был внутри; длина произвольного периода) — исправлены тесты, не код.

**Проверка:** `dotnet test` Core — 231 passed, 0 failed (парсер: 38 корректных + 33 ошибочных кейса из таблицы F1.4 и пограничных, плюс отдельные проверки); покрытие строк Core — 92,8%.
