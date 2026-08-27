BEGIN;

ALTER TABLE kfwd_seasons
  ADD COLUMN IF NOT EXISTS one_day_round_limit INTEGER NOT NULL DEFAULT 5;

ALTER TABLE kfwd_rewards
  ALTER COLUMN day_reward_claimed DROP DEFAULT;
ALTER TABLE kfwd_rewards
  ALTER COLUMN day_reward_claimed TYPE INTEGER
  USING CASE WHEN day_reward_claimed THEN 7 ELSE 0 END;
ALTER TABLE kfwd_rewards
  ALTER COLUMN day_reward_claimed SET DEFAULT 0;
ALTER TABLE kfwd_rewards
  ALTER COLUMN day_reward_claimed SET NOT NULL;

ALTER TABLE kfwd_rewards
  ALTER COLUMN day_ranking DROP DEFAULT;
ALTER TABLE kfwd_rewards
  ALTER COLUMN day_ranking TYPE INTEGER[]
  USING CASE WHEN day_ranking IS NULL THEN ARRAY[0,0,0]::INTEGER[] ELSE ARRAY[0,0,day_ranking]::INTEGER[] END;
ALTER TABLE kfwd_rewards
  ALTER COLUMN day_ranking SET DEFAULT ARRAY[0,0,0]::INTEGER[];
ALTER TABLE kfwd_rewards
  ALTER COLUMN day_ranking SET NOT NULL;

CREATE TABLE IF NOT EXISTS player_general_treasures(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  treasure_id INTEGER NOT NULL,
  goods_type INTEGER NOT NULL,
  quality INTEGER NOT NULL,
  leadership INTEGER NOT NULL,
  strength INTEGER NOT NULL,
  owner_general_id INTEGER,
  state SMALLINT NOT NULL DEFAULT 0,
  source TEXT NOT NULL,
  source_key TEXT NOT NULL UNIQUE,
  acquired_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_player_general_treasures_player ON player_general_treasures(player_id);

CREATE TABLE IF NOT EXISTS player_general_treasure_overflow(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  treasure_id INTEGER NOT NULL,
  goods_type INTEGER NOT NULL,
  quality INTEGER NOT NULL,
  leadership INTEGER NOT NULL,
  strength INTEGER NOT NULL,
  source TEXT NOT NULL,
  source_key TEXT NOT NULL UNIQUE,
  sell_time TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_player_general_treasure_overflow_player ON player_general_treasure_overflow(player_id);

COMMIT;
