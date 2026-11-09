# CLAUDE.md — WorkTime Tracker

Трекер рабочего времени для Windows на C# / .NET 10 / Avalonia 12. Пользователь вводит периоды
строкой (`0900 1800 созвон`) или через форму, смотрит статистику; данные — в локальной SQLite.
Код и UI собираются и тестируются на macOS/Linux, Windows-exe собирает GitHub Actions.

Подробности: `docs/README.md` (карта документации), задание — `docs/ORIGINAL_PROMPT.md`,
прогресс — `docs/PROGRESS.md`, история — `docs/DEVELOPMENT_LOG.md`.

## Команды

.NET 10 SDK на машине разработки может лежать в `~/.dotnet` → `export PATH=$HOME/.dotnet:$PATH`.

| Что | Команда |
|---|---|
| Сборка | `scripts/build.sh` (restore `--locked-mode` + `build -c Release`) |
| Тесты + покрытие | `scripts/test.sh` (порог покрытия Core 90%) |
| Запуск | `scripts/run.sh [--data-dir /tmp/wtt]` |
| Публикация exe | `scripts/publish-win.sh [версия]` → `artifacts/win-x64/WorkTimeTracker.exe` |
| Форматирование | `dotnet format` / проверка `dotnet format --verify-no-changes` |

## Карта кода

- `src/WorkTimeTracker.Core` — чистый домен без зависимостей.
- `src/WorkTimeTracker.Data` — SQLite (`Microsoft.Data.Sqlite` + Dapper), `DataPaths`.
- `src/WorkTimeTracker.App` — Avalonia: `Program.cs` (CLI-режимы `--version`, `--self-test`, `--data-dir`), `Views/`, `ViewModels/`, `Services/`.
- `tests/*` — xUnit v3; `App.Tests` — headless Avalonia.
- `tools/IconGen` — генератор иконки (SkiaSharp).

## Инварианты и подводные камни

- `TimeProvider` везде вместо `DateTime.Now` (в тестах — `FakeTimeProvider`).
- `TreatWarningsAsErrors=true`; compiled bindings по умолчанию — у каждого представления `x:DataType`.
- Без trimming/AOT (ADR-0006). Версии пакетов — только в `Directory.Packages.props`; lock-файлы коммитятся.
- Тесты headless UI используют xunit.v3 **3.2.2** — эту версию требует `Avalonia.Headless.XUnit 12.1.5`.

## Правила работы

- Любое изменение → тесты → `CHANGELOG.md` `[Unreleased]` → `docs/FEATURES.md` → профильный `docs/*.md` → запись в `docs/DEVELOPMENT_LOG.md`.
- Значимое решение → новый ADR в `docs/adr/`.
- Коммиты — Conventional Commits на русском с ID требований: `feat(F1): живой предпросмотр быстрого ввода`.
