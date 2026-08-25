BEGIN;
CREATE TABLE IF NOT EXISTS player_quest_claims(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  task_id INTEGER NOT NULL,
  claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,task_id)
);
COMMIT;
