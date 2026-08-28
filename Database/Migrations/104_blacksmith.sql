BEGIN;

CREATE TABLE IF NOT EXISTS player_blacksmith(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  smith_id INTEGER NOT NULL CHECK(smith_id > 0),
  level INTEGER NOT NULL DEFAULT 1 CHECK(level > 0),
  daily_used INTEGER NOT NULL DEFAULT 0 CHECK(daily_used >= 0),
  usage_date DATE NOT NULL DEFAULT CURRENT_DATE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,smith_id)
);

COMMIT;
