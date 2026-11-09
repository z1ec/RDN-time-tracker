# Журнал изменений

Все заметные изменения проекта документируются в этом файле.
Формат — [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/), версии — [SemVer](https://semver.org/lang/ru/).

## [Unreleased]

### Добавлено
- Разбор быстрого ввода `[дата] начало–конец [комментарий]` с ночными сменами, `сейчас`/`now`, многострочной вставкой и понятными ошибками (F1).
- Запрет пересечений записей с учётом соседних дней (F7.1).
- Расчёт статистики: итого, средние, норма и баланс, сравнение с предыдущим периодом, ряды для графиков (F3.2, F3.3).
- Экспорт в CSV для русского Excel (F3.5).
- Решение `WorkTimeTracker.slnx`: проекты Core / Data / App, три тестовых проекта, утилита `tools/IconGen`; Central Package Management, lock-файлы, `global.json` (.NET 10).
- Окно Avalonia с боковой навигацией; режимы командной строки `--version`, `--self-test`, `--data-dir` (F7.11).
- CI в GitHub Actions: тесты и покрытие на Linux, публикация и self-test настоящего Windows-exe.
- Скрипты `scripts/build.sh`, `test.sh`, `run.sh`, `publish-win.sh`.
- Инфраструктура проекта: лицензия MIT, `.gitignore`, `.gitattributes`, `.editorconfig`, журнал разработки и чек-лист прогресса.
