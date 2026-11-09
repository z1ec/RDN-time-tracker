# ADR-0001: Avalonia UI вместо WPF / WinForms / MAUI
- Статус: принято · Дата: 2026-11-09

## Контекст
Целевая платформа — Windows 10/11 x64, но разработка идёт на macOS без доступа к Windows-машине. Нужно уметь собирать, запускать, тестировать и снимать скриншоты интерфейса прямо на Mac и в Linux-CI, а итоговый exe собирать в GitHub Actions.

## Решение
UI на **Avalonia UI 12.1.5** с `FluentTheme`, шрифтом Inter (`Avalonia.Fonts.Inter`) и MVVM на CommunityToolkit.Mvvm. Compiled bindings включены по умолчанию (`AvaloniaUseCompiledBindingsByDefault`), у каждого представления задан `x:DataType`.

## Рассмотренные альтернативы
- **WPF** — нативно для Windows, но не собирается и не запускается на macOS/Linux: нельзя ни отлаживать UI, ни гонять UI-тесты в Linux-CI.
- **WinForms** — те же ограничения, плюс устаревший вид без тем.
- **.NET MAUI** — для desktop-Windows требует WinUI/Windows App SDK, сборка Windows-таргета на macOS невозможна; тяжелее и капризнее в CI.
- **Uno Platform** — работоспособно, но тяжелее по шаблону и инфраструктуре для маленького приложения.

## Последствия (плюсы / минусы / риски)
- ➕ Один код для всех ОС; приложение запускается на Mac (`scripts/run.sh`), headless-тесты (`Avalonia.Headless.XUnit`) и скриншоты с настоящим Skia работают на macOS и Linux.
- ➕ Fluent-тема даёт современный вид и светлую/тёмную темы «из коробки».
- ➖ Размер exe больше, чем у WPF (Skia + HarfBuzz в комплекте) — около 50 МБ в single-file.
- ⚠️ Риск: нативные библиотеки (Skia, HarfBuzz, SQLite) должны корректно распаковываться из single-file exe. Снимается self-test настоящего exe на `windows-latest` в каждом CI-запуске (ADR-0006).
