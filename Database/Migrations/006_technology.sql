BEGIN;

CREATE TABLE IF NOT EXISTS player_technologies(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  technology_id INTEGER NOT NULL,
  key_id INTEGER NOT NULL,
  injected_count INTEGER NOT NULL DEFAULT 0 CHECK(injected_count >= 0),
  status SMALLINT NOT NULL DEFAULT 0 CHECK(status BETWEEN 0 AND 5),
  research_complete_at TIMESTAMPTZ NULL,
  is_new BOOLEAN NOT NULL DEFAULT FALSE,
  finish_new BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,technology_id)
);

CREATE INDEX IF NOT EXISTS ix_player_technologies_status
  ON player_technologies(player_id,status,research_complete_at);
CREATE INDEX IF NOT EXISTS ix_player_technologies_key
  ON player_technologies(player_id,key_id,status);

COMMIT;
