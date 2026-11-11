# Модель данных и хранилище

Хранилище — один файл SQLite (`Microsoft.Data.Sqlite` + Dapper, ADR-0002). Код — `src/WorkTimeTracker.Data/`, тесты — `tests/WorkTimeTracker.Data.Tests/`.

## Где лежат файлы (F4.2, F4.3)

Папка данных выбирается в `DataPaths.Resolve` по приоритету:

| Приоритет | Условие | Папка |
|---|---|---|
| 1 | аргумент `--data-dir <путь>` | `<путь>` |
| 2 | рядом с exe лежит файл `portable.txt` (портативный режим) | `.\data\` рядом с exe |
| 3 | по умолчанию | `%LOCALAPPDATA%\WorkTimeTracker\` (`Environment.SpecialFolder.LocalApplicationData`) |

Содержимое папки данных:

```
WorkTimeTracker\
├── worktime.db            # база данных (+ worktime.db-wal, worktime.db-shm — журнал WAL)
├── backups\
│   └── worktime-YYYY-MM-DD.db   # резервные копии, последние 14
├── logs\
│   └── app-YYYYMMDD.log         # журнал программы (Serilog), последние 7 дней
├── self-test.log          # отчёт --self-test, если он запускался с --data-dir этой папки
└── self-test-db\          # отдельная БД режима --self-test (пользовательская не трогается)
```

`--self-test` пишет отчёт и свою БД в папку из `--data-dir`. Без `--data-dir` он работает во временной папке `%TEMP%\WorkTimeTracker-selftest`, а не в папке пользователя: случайный запуск проверки не должен трогать настоящие данные.

Путь к БД показывается в «Настройки → Данные» и «О программе».

## Схема

```sql
CREATE TABLE entries (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    work_date   TEXT    NOT NULL,                  -- 'yyyy-MM-dd', дата начала
    start_min   INTEGER NOT NULL CHECK (start_min BETWEEN 0 AND 1439),
    end_min     INTEGER NOT NULL,                  -- > 1440 → следующий день
    comment     TEXT    NOT NULL DEFAULT '',
    created_at  TEXT    NOT NULL,                  -- UTC, ISO-8601
    updated_at  TEXT    NOT NULL,                  -- UTC, ISO-8601
    deleted_at  TEXT    NULL,                      -- мягкое удаление
    CHECK (end_min > start_min AND end_min - start_min <= 1440),
    CHECK (length(comment) <= 500)
);
CREATE INDEX ix_entries_work_date  ON entries(work_date)  WHERE deleted_at IS NULL;      -- 0001
CREATE INDEX ix_entries_deleted_at ON entries(deleted_at) WHERE deleted_at IS NOT NULL;  -- 0002

CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE schema_version (version INTEGER NOT NULL, applied_at TEXT NOT NULL);

-- 0003: блокнот, 0004: строки блокнота (см. раздел «Блокнот» ниже)
CREATE TABLE notepad_sheets (...);
CREATE TABLE notepad_lines (...);   -- 0004 заменила notepad_items (0003)
```

### Смысл полей `entries`

| Поле | Значение |
|---|---|
| `work_date` | Дата **начала** записи. Ночная смена целиком относится к этой дате (F3.6, ADR-0003). |
| `start_min` | Начало в минутах от полуночи `work_date`, 0…1439. |
| `end_min` | Конец в минутах от полуночи `work_date`. `1440` — ровно полночь (24:00), больше 1440 — следующие сутки. |
| `comment` | Комментарий, до 500 символов, без пробелов по краям. |
| `created_at`, `updated_at` | Время создания и последнего изменения, UTC, формат `yyyy-MM-ddTHH:mm:ss.fffZ` (строки сравниваются лексикографически). |
| `deleted_at` | `NULL` — запись активна; иначе — время мягкого удаления (ADR-0004). **Все выборки приложения фильтруют `deleted_at IS NULL`.** |

Длительность = `end_min − start_min`.

### Примеры

| Ввод | `work_date` | `start_min` | `end_min` | Длительность |
|---|---|---|---|---|
| `0900 1800` (10.11.2026) | `2026-11-10` | 540 | 1080 | 9 ч |
| `2200 0200` | `2026-11-10` | 1320 | 1560 | 4 ч |
| `0000 2400` | `2026-11-10` | 0 | 1440 | 24 ч |
| `2300 0000` | `2026-11-10` | 1380 | 1440 | 1 ч |

### `settings`

| Ключ | Значение | По умолчанию |
|---|---|---|
| `daily_norm_minutes` | дневная норма в минутах, 0…1440 | `480` |
| `work_days` | рабочие дни через запятую, `DayOfWeek` (0 — воскресенье) | `1,2,3,4,5` |
| `theme` | `System` / `Light` / `Dark` | `System` |
| `window` | `X,Y,Width,Height,IsMaximized(0/1)` — положение окна (F4.8) | нет |

Повреждённое или неизвестное значение не приводит к ошибке — берётся значение по умолчанию (`SettingsStore`).

## Блокнот (F8, F9, миграции `0003` и `0004`)

Блокнот — песочница со своими строками (ADR-0009, ADR-0010). У строки свои дата, время и комментарий; `entry_id` связывает её с записью журнала. Связанная строка — время учтено в «Журнале» и «Статистике»; несвязанная — только в блокноте. Код — `NotepadRepository`, тесты — `NotepadRepositoryTests`, `NotepadMigrationTests`, `NotepadLinesMigrationTests`.

```sql
CREATE TABLE notepad_sheets (                                            -- 0003
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT    NOT NULL DEFAULT '' CHECK (length(title) <= 100),
    started_at  TEXT    NOT NULL,      -- UTC: создание листа (первая строка или ввод названия)
    cleared_at  TEXT    NULL,          -- UTC: «Очистить всё»; NULL = текущий лист
    deleted_at  TEXT    NULL           -- удалён из архива (мягко, для отмены)
);
CREATE UNIQUE INDEX ux_notepad_sheets_current ON notepad_sheets((1)) WHERE cleared_at IS NULL AND deleted_at IS NULL;

CREATE TABLE notepad_lines (                                             -- 0004
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    sheet_id    INTEGER NOT NULL REFERENCES notepad_sheets(id) ON DELETE CASCADE,
    work_date   TEXT    NOT NULL,                                       -- как в entries
    start_min   INTEGER NOT NULL CHECK (start_min BETWEEN 0 AND 1439),
    end_min     INTEGER NOT NULL,
    comment     TEXT    NOT NULL DEFAULT '' CHECK (length(comment) <= 500),
    entry_id    INTEGER NULL REFERENCES entries(id) ON DELETE CASCADE,  -- NULL = «не в статистике»
    added_at    TEXT    NOT NULL,                                       -- UTC
    updated_at  TEXT    NOT NULL,                                       -- UTC
    removed_at  TEXT    NULL,                                           -- убрана кнопкой ✕ (мягко, для отмены)
    CHECK (end_min > start_min AND end_min - start_min <= 1440)
);
CREATE INDEX ix_notepad_lines_sheet ON notepad_lines(sheet_id);
CREATE UNIQUE INDEX ux_notepad_lines_entry ON notepad_lines(entry_id) WHERE entry_id IS NOT NULL;
```

| Состояние листа | `cleared_at` | `deleted_at` |
|---|---|---|
| текущий (в него добавляются строки) | `NULL` | `NULL` |
| в архиве | время очистки | `NULL` |
| удалён из архива (можно отменить, через 30 дней удаляется физически) | время очистки | время удаления |

### Статусы строки (F9.3) — вычисляются, не хранятся

| Статус | Условие |
|---|---|
| в статистике | `entry_id` задан, и `work_date`, `start_min`, `end_min`, `comment` совпадают со связанной записью |
| не в статистике | `entry_id IS NULL` |
| в статистике другое время | `entry_id` задан, но значения отличаются (правка строки пересеклась с другой записью, F9.5) |

Сумма листа — по значениям строк; вторая цифра «в статистике» — по фактическому времени связанных записей (F9.4).

### Инварианты

- **Текущий лист — не больше одного** (уникальный индекс `ux_notepad_sheets_current`). Лист создаётся лениво — при первой строке или при вводе непустого названия.
- **Запись журнала связана максимум с одной строкой** (`ux_notepad_lines_entry`). Несвязанных строк с одинаковым временем может быть сколько угодно (F9.1).
- **Видимые строки листа:** `removed_at IS NULL AND (entry_id IS NULL OR entries.deleted_at IS NULL)`. Удаление записи в «Журнале» скрывает связанную строку, отмена удаления — возвращает; несвязанные строки это не затрагивает (F9.7).
- **В базу — только без пересечений (F9.2).** Строка и её новая запись создаются одной транзакцией (`NotepadRepository.AddLinesAsync`, `BEGIN IMMEDIATE`); многострочная вставка — по порядку в одной транзакции.
- **Правка строки (F9.5)** — одна транзакция `NotepadRepository.UpdateLineAsync`: только строка / строка и запись / строка и новая запись.
- **Правка записи в «Журнале» переносится в связанную строку** в той же транзакции (`EntryRepository.UpdateAsync`, F9.6).
- **✕, «Очистить всё», «Удалить из архива» и их отмена не изменяют и не удаляют строки `entries`.** `entries` меняют только добавление и правка строки — и только без пересечений.
- **Каскады:** физическое удаление записи (очистка через 30 дней) удаляет связанную строку; физическое удаление листа удаляет его строки, но не записи.

### Примеры

| Действие | Что меняется в БД |
|---|---|
| В блокноте введено `0900 1800 задача`, пересечений нет | `INSERT` в `entries`; при необходимости `INSERT` в `notepad_sheets`; `INSERT` в `notepad_lines` с `entry_id` — одна транзакция |
| То же время введено ещё раз | только `INSERT` в `notepad_lines` с `entry_id = NULL` |
| ✎ связанной строки, без пересечений | `UPDATE notepad_lines` и `UPDATE entries` — одна транзакция |
| ✎ связанной строки, с пересечением | только `UPDATE notepad_lines` (статус «в статистике другое время») |
| ✎ несвязанной строки, без пересечений | `UPDATE notepad_lines`, `INSERT` в `entries`, `notepad_lines.entry_id = новая` — одна транзакция |
| Правка записи в «Журнале» | `UPDATE entries` и `UPDATE notepad_lines … WHERE entry_id = …` — одна транзакция |
| ✕ у строки | `notepad_lines.removed_at = сейчас` |
| «Отменить» после ✕ | `notepad_lines.removed_at = NULL` |
| «Очистить всё» | `notepad_sheets.cleared_at = сейчас` |
| «Удалить из архива» | `notepad_sheets.deleted_at = сейчас` |
| Удаление записи в «Журнале» | только `entries.deleted_at = сейчас` |

### Миграция `0004_notepad_lines.sql` (v1.1.0 → v1.2.0)

В SQLite нельзя сменить первичный ключ через `ALTER`, поэтому `notepad_items` (первичный ключ — `entry_id`) пересоздаётся как `notepad_lines`. Всё — одна транзакция `Migrator`; перед миграцией, как всегда, делается резервная копия.

1. `CREATE TABLE notepad_lines`.
2. Каждая привязка `notepad_items` → строка: `sheet_id`, `entry_id`, `added_at`, `removed_at` сохраняются, `updated_at = added_at`, значения `work_date`, `start_min`, `end_min`, `comment` копируются из связанной записи `entries`. Переносятся и строки, чьи записи удалены в «Журнале»: видимость решает `entries.deleted_at`. После переноса у всех строк статус «в статистике».
3. Сверка: число строк `notepad_lines` должно равняться числу привязок. Если нет (привязка без записи — при `foreign_keys=ON` невозможна), временная таблица с `CHECK (ok = 1)` прерывает скрипт, и миграция откатывается целиком: `notepad_items` остаётся, программа сообщает об ошибке открытия БД (F4.7), данные не теряются.
4. `DROP TABLE notepad_items` (с его индексом), индексы `ix_notepad_lines_sheet`, `ux_notepad_lines_entry`.

Проверено тестами `NotepadLinesMigrationTests` (S27: БД 1.1.0 с текущим листом, архивом, удалённым листом, убранными и удалёнными строками — видимость построчно та же, дамп `entries` и листов не изменился) и `NotepadMigrationTests` (цепочка 1.0.0 → 1.2.0).

### Очистка мусора (F8.18)

При запуске, вместе с очисткой записей (`DatabaseInitializer`, тот же срок — 30 дней): `NotepadRepository.PurgeAsync` физически удаляет строки с `removed_at` старше срока и листы с `deleted_at` старше срока (их строки — каскадом). Строки, связанные с физически удалёнными записями, уходят каскадом от `entries`. Записи `entries` очистка блокнота не затрагивает.

## Соединения и надёжность (F4.1, F4.6, F4.7)

- `SqliteConnectionFactory` открывает короткоживущие соединения из пула с `PRAGMA journal_mode=WAL; foreign_keys=ON; busy_timeout=5000`.
- Вся работа с БД выполняется в пуле потоков (`RunAsync`), UI не блокируется.
- Каждое изменение (`AddAsync`, `UpdateAsync`, `SoftDeleteAsync`, …) — отдельная транзакция. Кнопки «Сохранить» нет: всё сразу на диске.
- Многострочное добавление — одна транзакция: либо все записи, либо ни одной (F1.2).
- Если БД не открывается (файл повреждён, нет доступа, схема новее программы), `DatabaseInitializer` бросает `DatabaseOpenException`; приложение показывает путь к файлу и кнопку «Открыть папку» и **ничего не удаляет и не пересоздаёт**.

## Миграции (F4.4)

- Скрипты — `src/WorkTimeTracker.Data/Migrations/NNNN_описание.sql`, встроены в сборку как embedded resources.
- При старте `Migrator` создаёт `schema_version` (если нет), читает `MAX(version)` и применяет скрипты с бо́льшим номером по возрастанию. Каждый скрипт и строка в `schema_version` — в одной транзакции: упавшая миграция откатывается целиком.
- Если версия БД больше известной программе — `DatabaseTooNewException` (открывать БД старой версией небезопасно).
- Номера идут подряд с 1; при загрузке это проверяется.

### Как добавить миграцию

1. Создайте `Migrations/0005_<что_делает>.sql` (имя: 4 цифры, `_`, строчные латинские буквы/цифры/`_`). Существующие файлы **никогда не редактируйте** — у пользователей они уже применены.
2. Пишите идемпотентно-безопасный SQL для схемы версии N−1; помните, что SQLite ограниченно поддерживает `ALTER TABLE` (для сложных изменений — новая таблица, копирование, переименование).
3. Добавьте тест в `MigratorTests` (версия N−1 → N) и обновите этот документ.

## Резервные копии (F4.5)

- **Автоматически:** при запуске, если копии за сегодня ещё нет, — копия **до** применения миграций.
- **Вручную:** «Настройки → Сделать резервную копию сейчас» (перезаписывает сегодняшнюю копию).
- Копия делается через SQLite Online Backup API (`SqliteConnection.BackupDatabase`) во временный файл, затем атомарно переименовывается в `backups\worktime-YYYY-MM-DD.db`.
- Хранятся 14 самых новых копий, старые удаляются (`BackupService.Rotate`).

### Ручное восстановление из копии

1. Закройте WorkTime Tracker.
2. Откройте папку данных («Настройки → Открыть папку» или `%LOCALAPPDATA%\WorkTimeTracker`).
3. Переименуйте текущие `worktime.db`, `worktime.db-wal`, `worktime.db-shm` (например, в `worktime.broken.db` и т. д.) — не удаляйте их.
4. Скопируйте нужный файл из `backups\` в папку данных и переименуйте его в `worktime.db`.
5. Запустите программу. При необходимости миграции применятся автоматически.

## Мягкое удаление и очистка (F5.3)

Удаление проставляет `deleted_at`; «Отменить» / `Ctrl+Z` сбрасывает его обратно. При каждом запуске записи, удалённые более 30 дней назад, удаляются физически (`PurgeDeletedAsync`). Подробнее — ADR-0004.

## Поиск по кириллице (F6.4)

Встроенные `LOWER`/`LIKE` в SQLite меняют регистр только у ASCII, поэтому `EntryRepository.SearchAsync` выбирает записи с непустым комментарием и фильтрует их в C# через `CompareInfo.IndexOf(..., CompareOptions.IgnoreCase)` культуры ru-RU: «Созвон» находит «созвон» и «СОЗВОН».
