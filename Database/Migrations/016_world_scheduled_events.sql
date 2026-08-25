BEGIN;
ALTER TABLE nation_event_npcs ADD COLUMN IF NOT EXISTS spawn_key TEXT NOT NULL DEFAULT '';
ALTER TABLE nation_event_npcs ADD COLUMN IF NOT EXISTS spawned_at TIMESTAMPTZ NOT NULL DEFAULT now();
ALTER TABLE nation_event_npcs ADD COLUMN IF NOT EXISTS defeated_at TIMESTAMPTZ NULL;
ALTER TABLE nation_event_npcs ADD COLUMN IF NOT EXISTS defeated_by BIGINT NULL REFERENCES players(id) ON DELETE SET NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_nation_event_npcs_spawn ON nation_event_npcs(scheduled_task_id,spawn_key);
CREATE INDEX IF NOT EXISTS ix_nation_event_npcs_active ON nation_event_npcs(force_id,city_id,event_type) WHERE defeated=false;
COMMIT;
