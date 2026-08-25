BEGIN;
CREATE TABLE IF NOT EXISTS player_team_time_grants(player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,source_key TEXT NOT NULL,amount INTEGER NOT NULL CHECK(amount>0),created_at TIMESTAMPTZ NOT NULL DEFAULT now(),PRIMARY KEY(player_id,source_key));
COMMIT;
