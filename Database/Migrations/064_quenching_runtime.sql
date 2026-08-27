BEGIN;

CREATE TABLE IF NOT EXISTS player_quenching_state(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  free_quenching_times INTEGER NOT NULL DEFAULT 0 CHECK(free_quenching_times >= 0),
  free_niubi_quenching_times INTEGER NOT NULL DEFAULT 0 CHECK(free_niubi_quenching_times >= 0),
  remind SMALLINT NOT NULL DEFAULT 0 CHECK(remind IN (0,1)),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE player_equipment
  ADD COLUMN IF NOT EXISTS quenching_times_free INTEGER NOT NULL DEFAULT 0 CHECK(quenching_times_free >= 0),
  ADD COLUMN IF NOT EXISTS special_skill_id INTEGER NOT NULL DEFAULT 0;

COMMIT;
