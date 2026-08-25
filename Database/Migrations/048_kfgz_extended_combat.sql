BEGIN;

CREATE TABLE IF NOT EXISTS player_battle_resources(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  recruit_token INTEGER NOT NULL DEFAULT 0 CHECK(recruit_token>=0),
  phantom_count INTEGER NOT NULL DEFAULT 0 CHECK(phantom_count>=0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE kfgz_signups
  ADD COLUMN IF NOT EXISTS recruit_token INTEGER NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS mubing INTEGER NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS phantom_count INTEGER NOT NULL DEFAULT 0;

ALTER TABLE player_generals
  ADD COLUMN IF NOT EXISTS forces_updated_at TIMESTAMPTZ NOT NULL DEFAULT now();

ALTER TABLE battle_units
  ADD COLUMN IF NOT EXISTS is_phantom BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS battle_phantom_grants(
  id BIGSERIAL PRIMARY KEY,
  battle_id BIGINT NOT NULL REFERENCES battles(id) ON DELETE CASCADE,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  request_key UUID NOT NULL,
  source_unit_id BIGINT NOT NULL REFERENCES battle_units(id) ON DELETE CASCADE,
  phantom_unit_id BIGINT NOT NULL UNIQUE REFERENCES battle_units(id) ON DELETE CASCADE,
  used_free BOOLEAN NOT NULL,
  gold_cost INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE(battle_id,player_id,request_key)
);
CREATE INDEX IF NOT EXISTS ix_battle_phantom_grants_player_battle
  ON battle_phantom_grants(player_id,battle_id);

COMMIT;
