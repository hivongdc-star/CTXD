BEGIN;
CREATE TABLE IF NOT EXISTS world_yellow_turban_states(
 slot_key TEXT PRIMARY KEY, coordinator_task_id BIGINT NOT NULL REFERENCES nation_scheduled_tasks(id) ON DELETE CASCADE,
 phase SMALLINT NOT NULL DEFAULT 1 CHECK(phase BETWEEN 1 AND 2), phase_started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 status SMALLINT NOT NULL DEFAULT 0 CHECK(status BETWEEN 0 AND 2), winner_force_id SMALLINT NULL, updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMIT;
