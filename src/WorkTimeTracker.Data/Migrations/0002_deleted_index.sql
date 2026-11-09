-- 0002: индекс для очистки мягко удалённых записей при запуске (F5.3).
CREATE INDEX ix_entries_deleted_at ON entries(deleted_at) WHERE deleted_at IS NOT NULL;
