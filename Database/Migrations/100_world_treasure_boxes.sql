BEGIN;

CREATE TABLE IF NOT EXISTS player_world_treasure_boxes(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  road_id INTEGER NOT NULL CHECK(road_id > 0),
  treasure_id INTEGER NOT NULL CHECK(treasure_id > 0),
  picked_at TIMESTAMPTZ NULL,
  PRIMARY KEY(player_id,road_id)
);

COMMIT;
