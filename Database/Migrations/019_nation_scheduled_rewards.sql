BEGIN;
CREATE TABLE IF NOT EXISTS nation_scheduled_reward_claims(
 slot_key TEXT NOT NULL, player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 task_type SMALLINT NOT NULL, rank INTEGER NOT NULL, exp INTEGER NOT NULL, iron INTEGER NOT NULL,
 claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(), PRIMARY KEY(slot_key,player_id)
);
COMMIT;
