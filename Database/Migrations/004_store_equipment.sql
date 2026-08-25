BEGIN;

ALTER TABLE players ADD COLUMN IF NOT EXISTS max_store_num INTEGER NOT NULL DEFAULT 30;
ALTER TABLE players ADD COLUMN IF NOT EXISTS intimacy INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS player_store(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  store_state INTEGER NOT NULL DEFAULT 1,
  style1_refresh_count INTEGER NOT NULL DEFAULT 0,
  style2_refresh_count INTEGER NOT NULL DEFAULT 0,
  next_style1_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  next_style2_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  locked_equipment_ids TEXT NOT NULL DEFAULT '',
  pending_refresh_style1 BOOLEAN NOT NULL DEFAULT FALSE,
  pending_refresh_style2 BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS player_store_offers(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  store_type SMALLINT NOT NULL CHECK(store_type IN (1,2)),
  position INTEGER NOT NULL CHECK(position BETWEEN 1 AND 6),
  equipment_id INTEGER NOT NULL,
  equipment_type INTEGER NOT NULL CHECK(equipment_type BETWEEN 1 AND 12),
  locked BOOLEAN NOT NULL DEFAULT FALSE,
  bought BOOLEAN NOT NULL DEFAULT FALSE,
  is_gold BOOLEAN NOT NULL DEFAULT FALSE,
  is_cheap BOOLEAN NOT NULL DEFAULT FALSE,
  price INTEGER NOT NULL DEFAULT 0,
  refresh_attribute INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE(player_id,store_type,position)
);
CREATE INDEX IF NOT EXISTS ix_store_offers_player_type ON player_store_offers(player_id,store_type);
CREATE INDEX IF NOT EXISTS ix_store_offers_equipment ON player_store_offers(player_id,equipment_id);

CREATE TABLE IF NOT EXISTS player_equipment(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  equipment_id INTEGER NOT NULL,
  goods_type INTEGER NOT NULL CHECK(goods_type BETWEEN 1 AND 12),
  level INTEGER NOT NULL DEFAULT 0,
  quality INTEGER NOT NULL DEFAULT 1,
  attribute INTEGER NOT NULL DEFAULT 0,
  owner_general_id INTEGER NULL,
  refresh_attribute INTEGER NOT NULL DEFAULT 0,
  gem_id INTEGER NOT NULL DEFAULT 0,
  quenching_times INTEGER NOT NULL DEFAULT 0,
  state INTEGER NOT NULL DEFAULT 0,
  num INTEGER NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_player_equipment_owner ON player_equipment(player_id,owner_general_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_player_equipment_general_slot
  ON player_equipment(player_id,owner_general_id,goods_type)
  WHERE owner_general_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS player_task_progress(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  task_id INTEGER NOT NULL,
  progress_key VARCHAR(64) NOT NULL,
  progress_value BIGINT NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,task_id,progress_key)
);

COMMIT;
