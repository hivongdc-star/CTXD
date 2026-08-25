BEGIN;
ALTER TABLE battle_units ADD COLUMN IF NOT EXISTS event_npc_id BIGINT NULL REFERENCES nation_event_npcs(id) ON DELETE SET NULL;
ALTER TABLE battle_units ADD COLUMN IF NOT EXISTS detached BOOLEAN NOT NULL DEFAULT FALSE;
CREATE TABLE IF NOT EXISTS nation_border_duel_ticks(
 scheduled_task_id BIGINT NOT NULL REFERENCES nation_scheduled_tasks(id) ON DELETE CASCADE,
 tick_key TEXT NOT NULL, battle_id BIGINT NOT NULL REFERENCES battles(id) ON DELETE CASCADE,
 duel_count INTEGER NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(scheduled_task_id,tick_key,battle_id)
);
COMMIT;
