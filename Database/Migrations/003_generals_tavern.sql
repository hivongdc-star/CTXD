BEGIN;

ALTER TABLE players ADD COLUMN IF NOT EXISTS sys_gold BIGINT NOT NULL DEFAULT 0;
ALTER TABLE players ADD COLUMN IF NOT EXISTS user_gold BIGINT NOT NULL DEFAULT 0;
ALTER TABLE players ADD COLUMN IF NOT EXISTS consume_level INTEGER NOT NULL DEFAULT 8;

CREATE TABLE IF NOT EXISTS player_generals(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_id INTEGER NOT NULL,
  general_type SMALLINT NOT NULL CHECK(general_type IN (1,2)),
  level INTEGER NOT NULL DEFAULT 1,
  exp BIGINT NOT NULL DEFAULT 0,
  leader_bonus INTEGER NOT NULL DEFAULT 0,
  strength_bonus INTEGER NOT NULL DEFAULT 0,
  intel_bonus INTEGER NOT NULL DEFAULT 0,
  politics_bonus INTEGER NOT NULL DEFAULT 0,
  forces INTEGER NOT NULL DEFAULT 0,
  location_id INTEGER NOT NULL DEFAULT 0,
  state SMALLINT NOT NULL DEFAULT 1,
  morale INTEGER NOT NULL DEFAULT 100,
  auto_state SMALLINT NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,general_id)
);
CREATE INDEX IF NOT EXISTS ix_player_generals_type ON player_generals(player_id,general_type);

CREATE TABLE IF NOT EXISTS player_tavern(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  tavern_state INTEGER NOT NULL DEFAULT 1,
  civil_refresh_time INTEGER NOT NULL DEFAULT 0,
  military_refresh_time INTEGER NOT NULL DEFAULT 0,
  next_civil_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  next_military_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  civil_defeated_info TEXT NOT NULL DEFAULT '',
  military_defeated_info TEXT NOT NULL DEFAULT '',
  locked_general_ids TEXT NOT NULL DEFAULT '',
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS player_tavern_offers(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_type SMALLINT NOT NULL CHECK(general_type IN (1,2)),
  position INTEGER NOT NULL CHECK(position BETWEEN 1 AND 5),
  general_id INTEGER NOT NULL,
  locked BOOLEAN NOT NULL DEFAULT FALSE,
  bought BOOLEAN NOT NULL DEFAULT FALSE,
  is_gold BOOLEAN NOT NULL DEFAULT FALSE,
  price INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE(player_id,general_type,position),
  UNIQUE(player_id,general_id)
);
CREATE INDEX IF NOT EXISTS ix_tavern_offers_player_type ON player_tavern_offers(player_id,general_type);

COMMIT;
