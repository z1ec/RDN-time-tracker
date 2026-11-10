-- 0003: блокнот (F8, ADR-0009) — листы и привязки записей к листам. Таблица entries не меняется.
CREATE TABLE notepad_sheets (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT    NOT NULL DEFAULT '' CHECK (length(title) <= 100),
    started_at  TEXT    NOT NULL,      -- UTC: создание листа (первая запись или ввод названия)
    cleared_at  TEXT    NULL,          -- UTC: «Очистить всё»; NULL = текущий лист
    deleted_at  TEXT    NULL           -- удалён из архива (мягко, для отмены)
);

-- Инвариант: текущий лист (не очищен и не удалён) — не больше одного.
CREATE UNIQUE INDEX ux_notepad_sheets_current ON notepad_sheets((1)) WHERE cleared_at IS NULL AND deleted_at IS NULL;

CREATE TABLE notepad_items (
    entry_id    INTEGER PRIMARY KEY REFERENCES entries(id) ON DELETE CASCADE,  -- запись — максимум в одном листе
    sheet_id    INTEGER NOT NULL REFERENCES notepad_sheets(id) ON DELETE CASCADE,
    added_at    TEXT    NOT NULL,      -- UTC
    removed_at  TEXT    NULL           -- убрана из блокнота кнопкой ✕ (мягко, для отмены)
);

CREATE INDEX ix_notepad_items_sheet ON notepad_items(sheet_id);
