BEGIN;

-- Legacy TaskRewardEquip writes reward equipment to STORE_HOUSE_SELL when StoreHouse is full.
-- This table preserves that exact overflow payload without changing the active equipment inventory API.
CREATE TABLE IF NOT EXISTS player_storehouse_sell(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  item_id INTEGER NOT NULL,
  type SMALLINT NOT NULL DEFAULT 1,
  goods_type INTEGER NOT NULL,
  level INTEGER NOT NULL,
  attribute TEXT NOT NULL DEFAULT '',
  quality INTEGER NOT NULL,
  sell_time TIMESTAMPTZ NOT NULL DEFAULT now(),
  gem_id INTEGER NOT NULL DEFAULT 0,
  num INTEGER NOT NULL DEFAULT 1,
  refresh_attribute TEXT NOT NULL DEFAULT '',
  quenching_times INTEGER NOT NULL DEFAULT 0,
  quenching_times_free INTEGER NOT NULL DEFAULT 0,
  special_skill_id INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_player_storehouse_sell_player
  ON player_storehouse_sell(player_id,type DESC,goods_type,quality DESC,level DESC,sell_time);

COMMIT;
