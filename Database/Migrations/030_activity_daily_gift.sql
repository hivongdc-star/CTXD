BEGIN;
ALTER TABLE player_resources ADD COLUMN IF NOT EXISTS worship INTEGER NOT NULL DEFAULT 0;
ALTER TABLE player_quest_runtime ADD COLUMN IF NOT EXISTS daily_events INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS player_daily_gift_claims(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 gift_day DATE NOT NULL,request_key TEXT NOT NULL,combo_id INTEGER NOT NULL,
 gold INTEGER NOT NULL,worship INTEGER NOT NULL,cards INTEGER[] NOT NULL,claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,gift_day),UNIQUE(player_id,request_key)
);
COMMIT;
