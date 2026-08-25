BEGIN;

CREATE TABLE IF NOT EXISTS nation_farm_state(
  force_id SMALLINT PRIMARY KEY REFERENCES nation_forces(force_id) ON DELETE CASCADE,
  level INTEGER NOT NULL DEFAULT 1 CHECK(level BETWEEN 1 AND 13),
  invest_sum BIGINT NOT NULL DEFAULT 0 CHECK(invest_sum>=0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
INSERT INTO nation_farm_state(force_id) VALUES(1),(2),(3) ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS player_farm_runtime(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  invest_cd_until TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS player_farms(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_id INTEGER NOT NULL,
  type SMALLINT NOT NULL CHECK(type BETWEEN 0 AND 3),
  started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  ends_at TIMESTAMPTZ NOT NULL,
  reward INTEGER NOT NULL CHECK(reward>=0),
  duration_minutes INTEGER NOT NULL CHECK(duration_minutes>0),
  UNIQUE(player_id,general_id),
  FOREIGN KEY(player_id,general_id) REFERENCES player_generals(player_id,general_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_player_farms_end ON player_farms(player_id,ends_at);

CREATE TABLE IF NOT EXISTS player_farm_buffs(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_id INTEGER NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY(player_id,general_id),
  FOREIGN KEY(player_id,general_id) REFERENCES player_generals(player_id,general_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS player_farm_gold_actions(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  request_key TEXT NOT NULL,
  action TEXT NOT NULL,
  general_id INTEGER NOT NULL DEFAULT 0,
  farm_type SMALLINT NOT NULL DEFAULT 0,
  reward INTEGER NOT NULL DEFAULT 0,
  gold_spent INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,request_key)
);

COMMIT;
