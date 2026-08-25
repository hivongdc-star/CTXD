BEGIN;
ALTER TABLE nation_forces ADD COLUMN IF NOT EXISTS task_event_serial INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS player_items(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 item_id INTEGER NOT NULL,
 item_type SMALLINT NOT NULL DEFAULT 5,
 quantity INTEGER NOT NULL DEFAULT 0 CHECK(quantity>=0),
 updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,item_id,item_type)
);
CREATE TABLE IF NOT EXISTS nation_scheduler_state(
 singleton SMALLINT PRIMARY KEY DEFAULT 1 CHECK(singleton=1),
 epoch_date DATE NOT NULL DEFAULT CURRENT_DATE,
 last_slot_key TEXT NOT NULL DEFAULT '',
 updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
INSERT INTO nation_scheduler_state(singleton) VALUES(1) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS nation_scheduled_tasks(
 id BIGSERIAL PRIMARY KEY, slot_key TEXT NOT NULL, force_id SMALLINT NOT NULL,
 task_type SMALLINT NOT NULL CHECK(task_type BETWEEN 1 AND 10), task_id INTEGER NOT NULL,
 target BIGINT NOT NULL DEFAULT 0, progress BIGINT NOT NULL DEFAULT 0,
 starts_at TIMESTAMPTZ NOT NULL, ends_at TIMESTAMPTZ NOT NULL,
 status SMALLINT NOT NULL DEFAULT 0 CHECK(status BETWEEN 0 AND 3),
 dependency_code TEXT NOT NULL DEFAULT '', event_serial INTEGER NOT NULL DEFAULT 0,
 UNIQUE(slot_key,force_id,task_id)
);
CREATE INDEX IF NOT EXISTS ix_nation_scheduled_tasks_active ON nation_scheduled_tasks(status,ends_at);
COMMIT;
