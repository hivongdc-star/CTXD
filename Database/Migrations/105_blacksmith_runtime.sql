BEGIN;

CREATE TABLE IF NOT EXISTS player_blacksmiths(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  smith_id SMALLINT NOT NULL CHECK(smith_id>0),
  level INTEGER NOT NULL DEFAULT 1 CHECK(level>0),
  daily_dissolve_usage INTEGER NOT NULL DEFAULT 0 CHECK(daily_dissolve_usage>=0),
  usage_day DATE NOT NULL DEFAULT CURRENT_DATE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,smith_id)
);

COMMIT;
