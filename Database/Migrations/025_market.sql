BEGIN;
ALTER TABLE player_quest_runtime ADD COLUMN IF NOT EXISTS recruit_tokens INTEGER NOT NULL DEFAULT 0;
ALTER TABLE player_quest_runtime ADD COLUMN IF NOT EXISTS market_buys INTEGER NOT NULL DEFAULT 0;
ALTER TABLE player_quest_runtime ADD COLUMN IF NOT EXISTS black_market_visits INTEGER NOT NULL DEFAULT 0;
ALTER TABLE player_quest_runtime ADD COLUMN IF NOT EXISTS black_market_buys INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS player_market(
 player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
 can_buy NUMERIC(10,2) NOT NULL DEFAULT 10,
 offers INTEGER[] NOT NULL DEFAULT '{}', refresh_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 buy_accrued_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS player_market_purchases(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 request_key TEXT NOT NULL, product_id INTEGER NOT NULL, item_type TEXT NOT NULL,
 item_num INTEGER NOT NULL, cost_type TEXT NOT NULL, cost_num INTEGER NOT NULL,
 created_at TIMESTAMPTZ NOT NULL DEFAULT now(), PRIMARY KEY(player_id,request_key)
);
CREATE TABLE IF NOT EXISTS player_black_market(
 player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
 cooldown_until TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS player_black_market_trades(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 request_key TEXT NOT NULL,left_type SMALLINT NOT NULL,right_type SMALLINT NOT NULL,
 spent INTEGER NOT NULL,received INTEGER NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,request_key)
);
CREATE TABLE IF NOT EXISTS player_black_market_recoveries(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 request_key TEXT NOT NULL,gold_spent INTEGER NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,request_key)
);
COMMIT;
