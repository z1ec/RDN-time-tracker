# Разработка

## Требования к окружению

| ОС | Что нужно | Что можно делать |
|---|---|---|
| **macOS** (основная ОС разработки) | .NET 10 SDK | сборка, тесты, запуск UI, скриншоты, публикация Windows-exe (кросс-публикация) |
| **Windows 10/11** | .NET 10 SDK; Git Bash для `scripts/*.sh` (или те же команды `dotnet` в PowerShell) | всё то же + запуск настоящего exe |
| **Linux** | .NET 10 SDK, `libfontconfig1` (для Skia в headless-тестах) | сборка, тесты, скриншоты, публикация exe |

Версия SDK закреплена в `global.json` (`10.0.100`, `rollForward: latestFeature` — подходит любой 10.0.1xx и новее в линейке 10.0).

### Установка .NET 10 SDK на macOS

Вариант 1 — без прав администратора, в домашнюю папку:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"   # добавьте в ~/.zshrc
dotnet --list-sdks
```

Вариант 2 — Homebrew:

```bash
brew install --cask dotnet-sdk
```

На Windows — установщик с https://dotnet.microsoft.com/download/dotnet/10.0 или `winget install Microsoft.DotNet.SDK.10`.

## Команды

| Задача | Команда |
|---|---|
| Сборка (Release, предупреждения = ошибки) | `scripts/build.sh` |
| Все тесты + покрытие + порог Core 90% | `scripts/test.sh` (переменные: `COVERAGE_THRESHOLD`, `BUILD_ARGS=--no-build`) |
| Запуск приложения | `scripts/run.sh` или `scripts/run.sh --data-dir /tmp/wtt` (отдельная папка данных для экспериментов) |
| Скриншоты для README | `scripts/screenshots.sh` → `docs/images/*.png` (`SCREENSHOTS_DIR=…` — в другую папку) |
| Windows-exe | `scripts/publish-win.sh [версия]` → `artifacts/win-x64/WorkTimeTracker.exe` |
| Упаковка релиза локально | `scripts/package-release.sh v1.2.3` → `artifacts/release/` |
| Заметки к релизу | `scripts/release-notes.sh 1.2.3` |
| Форматирование | `dotnet format` · проверка: `dotnet format --verify-no-changes` |
| Иконка | `dotnet run --project tools/IconGen` → `src/WorkTimeTracker.App/Assets/` |
| Версия / self-test без UI | `dotnet run --project src/WorkTimeTracker.App -- --version` · `-- --self-test --data-dir /tmp/wtt-st` |

Отдельные наборы тестов: `dotnet test tests/WorkTimeTracker.Core.Tests`, фильтр по имени — `--filter "FullyQualifiedName~QuickEntryParser"`.

## Тесты

| Проект | Что проверяет |
|---|---|
| `WorkTimeTracker.Core.Tests` | парсер (таблица F1.4 и пограничные случаи), правила и пересечения, периоды, статистика на фиксированных наборах с `FakeTimeProvider`, CSV, форматы, `EntryService` на хранилище в памяти |
| `WorkTimeTracker.Data.Tests` | миграции, CRUD, мягкое удаление и очистка, бэкапы и ротация, настройки, поиск по кириллице, конкурентная запись, повреждённый файл — на временных файлах БД |
| `WorkTimeTracker.App.Tests` | ViewModel-тесты (с настоящей SQLite во временной папке и фейковыми диалогами), headless UI smoke (окно, ввод `0900 1800` + Enter, горячие клавиши, диалоги), self-test, скриншоты (категория `Screenshots`, в обычном прогоне исключены) |

- «Сегодня» в тестах фиксировано через `FakeTimeProvider` (10.11.2026, для скриншотов — 12.11.2026). Никакого `DateTime.Now` в коде.
- xUnit v3 **3.2.2** — именно эту версию требует `Avalonia.Headless.XUnit 12.1.5`.
- Headless-тесты (`[AvaloniaFact]`) используют настоящий Skia (`UseHeadlessDrawing = false`) и шрифт Inter; приложение настраивается в `TestAppBuilder`.
- Покрытие собирает coverlet; `scripts/coverage_summary.py` печатает таблицу и проверяет порог для Core.

## Соглашения

### Код
- Идентификаторы — по-английски, комментарии, XML-документация, тексты UI и сообщения — по-русски.
- `Nullable=enable`, `TreatWarningsAsErrors=true`, file-scoped namespaces, приватные поля `_camelCase` (`.editorconfig`).
- Время — только через `TimeProvider`; отображение — через `RuFormat` / `DurationFormatter`.
- Бизнес-правила — только в Core. Новое правило ввода → `EntryRules`/`QuickEntryParser` + тест в таблице.
- Все выборки из `entries` — с `deleted_at IS NULL`.
- Схема БД меняется только новой миграцией (`docs/DATA_MODEL.md`).
- Версии пакетов — только в `Directory.Packages.props`; после изменения — `dotnet restore --force-evaluate` и коммит lock-файлов.

### Коммиты
Conventional Commits на русском с ID требований из `docs/FEATURES.md`:

```
feat(F1): живой предпросмотр быстрого ввода
fix(F3.2): сравнение текущего месяца с тем же числом дней
docs: формулы статистики
build: обновление Avalonia до 12.1.6
test(F7.1): пересечение ночной смены с утром следующего дня
```

Работа идёт в `main`; каждый коммит собирается и проходит тесты. После изменения — обновить `CHANGELOG.md` (`[Unreleased]`), `docs/FEATURES.md`, профильный документ и `docs/DEVELOPMENT_LOG.md`; значимое решение — новый ADR.

## Ручная проверка на Windows (5 минут)

1. Скачать `WorkTimeTracker-vX.Y.Z-win-x64.exe` из Releases (или артефакт CI) и запустить. Если SmartScreen предупреждает — «Подробнее» → «Выполнить в любом случае».
2. Окно открылось, курсор — в поле быстрого ввода; иконка в заголовке и на панели задач.
3. Ввести `0900 1800 проверка` → под полем зелёный предпросмотр → `Enter` → запись появилась в списке, сводка «Сегодня: 9 ч 00 мин».
4. Ввести `0900 2500` → красная ошибка «Часы должны быть от 0 до 23», кнопка «Добавить» неактивна.
5. Вставить из буфера две строки `1900 2000` и `2200 0200 ночь` → «✓ 2 записи» → `Enter` → у ночной записи отметка «+1».
6. Ввести `1000 1100` → ошибка «Пересекается с записью …».
7. Выделить запись, нажать `Del` → уведомление «Удалено записей: 1 · Отменить» → `Ctrl+Z` → запись вернулась.
8. Двойной клик по записи → изменить комментарий → «Сохранить».
9. `Ctrl+F` → ввести часть комментария заглавными буквами — запись находится.
10. `Ctrl+3` → статистика за неделю: KPI, график по дням с линией нормы; переключить «Месяц», «Год» (тепловая карта); «CSV» → открыть файл в Excel — кириллица читается, колонки разделены.
11. `Ctrl+4` → сменить тему на «Тёмная» → вид аккуратный; «Сделать резервную копию сейчас» → «Открыть папку копий».
12. Закрыть и снова открыть программу — записи на месте, размер и положение окна сохранены, тема та же.
13. `%LOCALAPPDATA%\WorkTimeTracker` содержит `worktime.db`, `backups\`, `logs\app-YYYYMMDD.log`.
14. Портативный режим: положить рядом с exe пустой `portable.txt` → запустить → данные создаются в `.\data\` рядом с exe.
15. В PowerShell (exe — оконное приложение, поэтому ждём его явно):
    ```powershell
    .\WorkTimeTracker.exe --version | Out-String          # WorkTime Tracker X.Y.Z
    $p = Start-Process .\WorkTimeTracker.exe -ArgumentList '--self-test','--data-dir',"$env:TEMP\wtt" -Wait -PassThru
    $p.ExitCode                                           # 0
    Get-Content "$env:TEMP\wtt\self-test.log"              # OK … РЕЗУЛЬТАТ: УСПЕХ
    ```

## Блокнот: ручная проверка на Windows (3 минуты)

Сценарии S1, S2, S5, S6, S8 из задания на v1.1.0 (`docs/prompts/2026-11-10-notepad.md`).

1. **S1.** `Ctrl+2` → открылась вкладка «Блокнот», курсор в поле ввода. Ввести `0900 1230 задача А`, `Enter`, затем `1330 1800 задача Б`, `Enter` → на плашке «В блокноте: 8 ч 00 мин · 2 записи · ведётся с …». `Ctrl+1` → обе записи есть в журнале; `Ctrl+3` → за сегодня 8 ч.
2. **S5.** `Ctrl+2` → нажать ✕ у «задача А» → запись исчезла из блокнота, уведомление «Убрано из блокнота: 1 запись · Отменить»; в журнале (`Ctrl+1`) она на месте. Вернуться (`Ctrl+2`), нажать `Ctrl+Z` → запись снова в блокноте.
3. **S6.** `Ctrl+1` → удалить «задача Б» (`Del`) → `Ctrl+2`: в блокноте осталась одна запись, сумма 3 ч 30 мин. `Ctrl+1` → `Ctrl+Z` → `Ctrl+2`: запись вернулась, сумма снова 8 ч 00 мин.
4. **S2.** В поле «Без названия» ввести `Заказ: сайт кафе`, `Enter`. Нажать «Очистить всё» → блокнот пуст, уведомление «Блокнот перенесён в архив · Отменить». Переключатель «Архив (1)» → карточка «Заказ: сайт кафе» с датой, суммой 8 ч 00 мин и временем очистки; клик по карточке раскрывает записи. `Ctrl+3` → статистика не изменилась.
5. **S8.** Вернуться на «Текущий», добавить `1900 2000`, нажать «Очистить всё» и сразу «Отменить» (или `Ctrl+Z`) → запись снова в блокноте, в архиве по-прежнему один лист.
6. Закрыть и открыть программу → `Ctrl+2`: текущий лист и архив на месте.
