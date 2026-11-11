-- 0004: блокнот-песочница (F9, ADR-0010). У строки блокнота свои дата, время и комментарий;
-- entry_id связывает её с записью журнала (NULL = «не в статистике»). notepad_items пересоздаётся как
-- notepad_lines: первичный ключ в SQLite через ALTER не поменять. Вся миграция — одна транзакция (Migrator).
CREATE TABLE notepad_lines (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    sheet_id    INTEGER NOT NULL REFERENCES notepad_sheets(id) ON DELETE CASCADE,
    work_date   TEXT    NOT NULL,                                       -- как в entries: 'yyyy-MM-dd', дата начала
    start_min   INTEGER NOT NULL CHECK (start_min BETWEEN 0 AND 1439),
    end_min     INTEGER NOT NULL,
    comment     TEXT    NOT NULL DEFAULT '' CHECK (length(comment) <= 500),
    entry_id    INTEGER NULL REFERENCES entries(id) ON DELETE CASCADE,  -- NULL = «не в статистике»
    added_at    TEXT    NOT NULL,                                       -- UTC
    updated_at  TEXT    NOT NULL,                                       -- UTC
    removed_at  TEXT    NULL,                                           -- убрана кнопкой ✕ (мягко, для отмены)
    CHECK (end_min > start_min AND end_min - start_min <= 1440)
);

-- Перенос: каждая привязка → строка со значениями связанной записи (статус «в статистике»).
-- Записи, удалённые в «Журнале», тоже переносятся: строка скрыта, пока запись удалена, и вернётся при отмене.
INSERT INTO notepad_lines (sheet_id, work_date, start_min, end_min, comment, entry_id, added_at, updated_at, removed_at)
SELECT i.sheet_id, e.work_date, e.start_min, e.end_min, e.comment, i.entry_id, i.added_at, i.added_at, i.removed_at
FROM notepad_items i
JOIN entries e ON e.id = i.entry_id
ORDER BY i.added_at, i.entry_id;

-- Защита от потери данных: перенесено ровно столько строк, сколько было привязок. Иначе CHECK
-- прерывает скрипт, и Migrator откатывает миграцию целиком — notepad_items остаётся на месте.
CREATE TEMP TABLE migration_0004_check (ok INTEGER NOT NULL CHECK (ok = 1));
INSERT INTO migration_0004_check (ok)
SELECT (SELECT count(*) FROM notepad_lines) = (SELECT count(*) FROM notepad_items);
DROP TABLE migration_0004_check;

DROP TABLE notepad_items;

CREATE INDEX ix_notepad_lines_sheet ON notepad_lines(sheet_id);

-- Инвариант: запись журнала связана максимум с одной строкой блокнота.
CREATE UNIQUE INDEX ux_notepad_lines_entry ON notepad_lines(entry_id) WHERE entry_id IS NOT NULL;
