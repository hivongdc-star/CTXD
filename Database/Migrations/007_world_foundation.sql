BEGIN;

CREATE TABLE IF NOT EXISTS player_world(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  discovered_city_ids INTEGER[] NOT NULL DEFAULT '{}',
  attackable_city_ids INTEGER[] NOT NULL DEFAULT '{}',
  focus_general_id INTEGER NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS player_world_moves(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_id INTEGER NOT NULL,
  road_id INTEGER NOT NULL,
  from_city_id INTEGER NOT NULL,
  to_city_id INTEGER NOT NULL,
  started_at TIMESTAMPTZ NOT NULL,
  arrives_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY(player_id,general_id),
  FOREIGN KEY(player_id,general_id) REFERENCES player_generals(player_id,general_id) ON DELETE CASCADE,
  CHECK(from_city_id <> to_city_id),
  CHECK(arrives_at >= started_at)
);
CREATE INDEX IF NOT EXISTS ix_player_world_moves_arrival ON player_world_moves(arrives_at);

COMMIT;
