BEGIN;

CREATE TABLE IF NOT EXISTS world_mine_holdings(
  mine_id INTEGER PRIMARY KEY,
  mine_type SMALLINT NOT NULL CHECK(mine_type BETWEEN 1 AND 4),
  owner_player_id BIGINT REFERENCES players(id) ON DELETE CASCADE,
  owner_force_id SMALLINT,
  mode SMALLINT NOT NULL DEFAULT 1 CHECK(mode IN(1,2)),
  started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  rush_at TIMESTAMPTZ,
  ends_at TIMESTAMPTZ,
  defender_player_ids BIGINT[] NOT NULL DEFAULT '{}',
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CHECK((mine_type IN(2,4) AND owner_player_id IS NOT NULL AND owner_force_id IS NULL AND ends_at IS NOT NULL)
     OR (mine_type IN(1,3) AND owner_player_id IS NULL AND owner_force_id IS NOT NULL AND ends_at IS NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_world_mine_personal_owner_type
  ON world_mine_holdings(owner_player_id,mine_type) WHERE owner_player_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_world_mine_due
  ON world_mine_holdings(ends_at) WHERE ends_at IS NOT NULL;

CREATE TABLE IF NOT EXISTS world_mine_battles(
  battle_id BIGINT PRIMARY KEY REFERENCES world_battle_handoffs(id) ON DELETE CASCADE,
  mine_id INTEGER NOT NULL,
  expected_owner_player_id BIGINT,
  expected_owner_force_id SMALLINT,
  resolved_at TIMESTAMPTZ
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_world_mine_active_battle
  ON world_mine_battles(mine_id) WHERE resolved_at IS NULL;

CREATE TABLE IF NOT EXISTS player_force_mine_harvests(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  mine_type SMALLINT NOT NULL CHECK(mine_type IN(1,3)),
  claimed_on DATE NOT NULL,
  output INTEGER NOT NULL CHECK(output>0),
  PRIMARY KEY(player_id,mine_type,claimed_on)
);

COMMIT;
