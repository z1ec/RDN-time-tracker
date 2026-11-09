-- 0001: начальная схема (docs/DATA_MODEL.md). Миграции только добавляются, существующие не редактируются.
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

CREATE INDEX ix_entries_work_date ON entries(work_date) WHERE deleted_at IS NULL;

CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
