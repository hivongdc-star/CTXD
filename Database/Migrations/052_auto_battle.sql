BEGIN;

CREATE TABLE IF NOT EXISTS player_auto_battles(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  force_id SMALLINT NOT NULL DEFAULT 0,
  target_city_id INTEGER NOT NULL DEFAULT 0,
  state SMALLINT NOT NULL DEFAULT 0,
  auto_type SMALLINT NOT NULL DEFAULT 0,
  exp BIGINT NOT NULL DEFAULT 0,
  lost BIGINT NOT NULL DEFAULT 0,
  result SMALLINT NOT NULL DEFAULT 0,
  baseline_exp BIGINT NOT NULL DEFAULT 0,
  baseline_lost BIGINT NOT NULL DEFAULT 0,
  started_at TIMESTAMPTZ,
  ends_at TIMESTAMPTZ,
  need_check_at TIMESTAMPTZ,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_player_auto_battles_active
  ON player_auto_battles(need_check_at)
  WHERE state=1;

COMMIT;
