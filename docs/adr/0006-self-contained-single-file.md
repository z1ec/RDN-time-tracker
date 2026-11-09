# ADR-0006: Self-contained single-file exe без trimming и без AOT
- Статус: принято · Дата: 2026-11-09

## Контекст
Пользователь скачивает приложение из GitHub Releases и запускает его на Windows без установки чего-либо ещё и без прав администратора. Установщик в v1.0.0 не делается.

## Решение
Публикация `win-x64`, `--self-contained true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`, `PublishTrimmed=false`, `DebugType=none`. Команда одна и та же в `scripts/publish-win.sh`, `ci.yml` и `release.yml`. Нативные `.pdb` SkiaSharp/HarfBuzz исключаются из публикации отдельным MSBuild-таргетом.

Корректность упаковки проверяется **запуском настоящего exe** в режиме `--self-test` на `windows-latest` в каждом CI-запуске и при релизе.

## Рассмотренные альтернативы
- **Framework-dependent** — exe меньше, но пользователю нужно ставить .NET 10 Desktop Runtime.
- **Trimming** — ломает рефлексию (Dapper, XAML-загрузчик Avalonia, DI); выигрыш ~20 МБ не стоит риска падений, которые проявятся только на машине пользователя.
- **Native AOT** — Avalonia поддерживает, но Dapper и часть зависимостей требуют дополнительной настройки; кросс-компиляция AOT с macOS под Windows невозможна.
- **MSIX / MSI / Inno Setup** — вне рамок v1.0.0 (см. ROADMAP).

## Последствия
- ➕ Один файл, ничего не нужно устанавливать; публикация работает кросс-платформенно (на macOS — несколько секунд).
- ➖ Размер ≈ 50 МБ (сжатый single-file); при первом запуске нативные библиотеки распаковываются во временную папку пользователя.
- ⚠️ Неподписанный exe вызывает предупреждение SmartScreen — описано в README и `docs/BUILD_AND_RELEASE.md`.
