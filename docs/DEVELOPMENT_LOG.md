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
