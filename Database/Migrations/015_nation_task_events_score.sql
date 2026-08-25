BEGIN;
CREATE TABLE IF NOT EXISTS nation_task_events(
 task_id BIGINT NOT NULL REFERENCES nation_scheduled_tasks(id) ON DELETE CASCADE,
 event_key TEXT NOT NULL, player_id BIGINT NULL REFERENCES players(id) ON DELETE SET NULL,
 value BIGINT NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(task_id,event_key)
);
CREATE TABLE IF NOT EXISTS player_scores(
 player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
 occupy_count INTEGER NOT NULL DEFAULT 0, assist_count INTEGER NOT NULL DEFAULT 0,
 cheer_count INTEGER NOT NULL DEFAULT 0, score BIGINT NOT NULL DEFAULT 0,
 updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS player_score_events(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 event_key TEXT NOT NULL, score INTEGER NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,event_key)
);
CREATE TABLE IF NOT EXISTS nation_event_npcs(
 id BIGSERIAL PRIMARY KEY, scheduled_task_id BIGINT NOT NULL REFERENCES nation_scheduled_tasks(id) ON DELETE CASCADE,
 force_id SMALLINT NOT NULL, event_type SMALLINT NOT NULL, city_id INTEGER NOT NULL,
 army_id INTEGER NOT NULL, defeated BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS ix_nation_event_npcs_city ON nation_event_npcs(city_id,defeated);
COMMIT;
