BEGIN;

CREATE TABLE IF NOT EXISTS world_cities(
  city_id INTEGER PRIMARY KEY,
  owner_force_id SMALLINT NOT NULL DEFAULT 0 CHECK(owner_force_id BETWEEN 0 AND 103),
  state SMALLINT NOT NULL DEFAULT 0,
  title SMALLINT NOT NULL DEFAULT 0,
  border SMALLINT NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE player_world_moves ADD COLUMN IF NOT EXISTS path_city_ids INTEGER[] NOT NULL DEFAULT '{}';
ALTER TABLE player_world_moves ADD COLUMN IF NOT EXISTS path_index INTEGER NOT NULL DEFAULT 1;

CREATE TABLE IF NOT EXISTS world_battle_handoffs(
  id BIGSERIAL PRIMARY KEY,
  city_id INTEGER NOT NULL,
  attacker_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  attacker_general_id INTEGER NOT NULL,
  attacker_force_id SMALLINT NOT NULL,
  defender_force_id SMALLINT NOT NULL,
  battle_type SMALLINT NOT NULL DEFAULT 3,
  status SMALLINT NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  resolved_at TIMESTAMPTZ NULL,
  FOREIGN KEY(attacker_player_id,attacker_general_id)
    REFERENCES player_generals(player_id,general_id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_world_battle_active_general
  ON world_battle_handoffs(attacker_player_id,attacker_general_id) WHERE status=0;
CREATE INDEX IF NOT EXISTS ix_world_battle_active_city
  ON world_battle_handoffs(city_id) WHERE status=0;

COMMIT;
