BEGIN;
CREATE TABLE IF NOT EXISTS player_treasures(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  treasure_id INTEGER NOT NULL,
  acquired_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  source TEXT NOT NULL,
  PRIMARY KEY(player_id,treasure_id)
);
COMMIT;
