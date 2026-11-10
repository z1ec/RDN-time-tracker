# Карта документации

| Файл | Для кого | О чём |
|---|---|---|
| [`../README.md`](../README.md) | все | витрина проекта: возможности, скриншоты, быстрый старт, сборка |
| [`../CLAUDE.md`](../CLAUDE.md) | агент | краткая память проекта: команды, карта кода, инварианты, правила работы |
| [`../CHANGELOG.md`](../CHANGELOG.md) | все | история версий (Keep a Changelog) |
| [`ORIGINAL_PROMPT.md`](ORIGINAL_PROMPT.md) | история | дословное техническое задание, по которому создан проект |
| [`prompts/`](prompts/) | история | дословные задания на доработки (v1.1.0 — [«Блокнот»](prompts/2026-11-10-notepad.md)) |
| [`DEVELOPMENT_LOG.md`](DEVELOPMENT_LOG.md) | история | журнал создания по этапам: решения, проблемы, проверки, коммиты |
| [`PROGRESS.md`](PROGRESS.md) | агент | чек-лист этапов и Definition of Done, точка возобновления работы |
| [`FEATURES.md`](FEATURES.md) | все | матрица «требование → статус → код → тесты» |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | разработчик | слои, поток данных, ключевые классы, MVVM, DI, потоки, ошибки, дизайн |
| [`DATA_MODEL.md`](DATA_MODEL.md) | разработчик | схема БД, файлы данных, миграции, бэкапы и восстановление |
| [`INPUT_FORMAT.md`](INPUT_FORMAT.md) | пользователь, разработчик | грамматика быстрого ввода, правила, тексты ошибок, примеры |
| [`STATISTICS.md`](STATISTICS.md) | разработчик | точные формулы показателей, нормы, сравнения и графиков |
| [`DEVELOPMENT.md`](DEVELOPMENT.md) | разработчик | окружение, команды, тесты, соглашения, ручная проверка на Windows |
| [`BUILD_AND_RELEASE.md`](BUILD_AND_RELEASE.md) | разработчик | CI, публикация exe, выпуск версии, SmartScreen, что делать при падении CI |
| [`ROADMAP.md`](ROADMAP.md) | все | идеи на будущее и известные ограничения |
| [`adr/`](adr/) | все | архитектурные решения (ADR) |
| [`images/`](images/) | все | скриншоты для README (генерируются `scripts/screenshots.sh`) |

## Архитектурные решения

| ADR | Решение |
|---|---|
| [0001](adr/0001-avalonia.md) | Avalonia UI вместо WPF / WinForms / MAUI |
| [0002](adr/0002-sqlite-dapper.md) | SQLite + Dapper |
| [0003](adr/0003-time-model.md) | время в минутах от начала даты, ночные смены, без часовых поясов |
| [0004](adr/0004-soft-delete-undo.md) | мягкое удаление и отмена вместо подтверждения |
| [0005](adr/0005-custom-charts.md) | свои лёгкие графики |
| [0006](adr/0006-self-contained-single-file.md) | self-contained single-file exe без trimming |
| [0007](adr/0007-no-overlaps.md) | запрет пересечений записей |
| [0008](adr/0008-dotnet-10-lts.md) | .NET 10 LTS вместо .NET 11 |
