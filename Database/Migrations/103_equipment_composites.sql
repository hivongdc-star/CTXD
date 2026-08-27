BEGIN;

CREATE TABLE IF NOT EXISTS player_equipment_composites(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  item_id INTEGER NOT NULL,
  composite_type SMALLINT NOT NULL CHECK(composite_type IN (10,14)),
  owner_general_id INTEGER NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_equipment_composites_player ON player_equipment_composites(player_id,composite_type,item_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_equipment_composites_general
  ON player_equipment_composites(player_id,owner_general_id)
  WHERE owner_general_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS player_equipment_composite_backup(
  id BIGSERIAL PRIMARY KEY,
  composite_id BIGINT NOT NULL REFERENCES player_equipment_composites(id) ON DELETE CASCADE,
  group_index SMALLINT NOT NULL CHECK(group_index IN (0,1)),
  slot_index SMALLINT NOT NULL CHECK(slot_index BETWEEN 1 AND 6),
  equipment_id INTEGER NOT NULL,
  goods_type INTEGER NOT NULL CHECK(goods_type BETWEEN 1 AND 6),
  level INTEGER NOT NULL DEFAULT 0,
  quality INTEGER NOT NULL DEFAULT 1,
  attribute INTEGER NOT NULL DEFAULT 0,
  refresh_attribute TEXT NOT NULL DEFAULT '',
  gem_id INTEGER NOT NULL DEFAULT 0,
  quenching_times INTEGER NOT NULL DEFAULT 0 CHECK(quenching_times >= 0),
  quenching_times_free INTEGER NOT NULL DEFAULT 0 CHECK(quenching_times_free >= 0),
  special_skill_id INTEGER NOT NULL DEFAULT 0,
  state INTEGER NOT NULL DEFAULT 0,
  num INTEGER NOT NULL DEFAULT 1 CHECK(num > 0),
  UNIQUE(composite_id,group_index,slot_index)
);
CREATE INDEX IF NOT EXISTS ix_equipment_composite_backup_parent
  ON player_equipment_composite_backup(composite_id,group_index,slot_index);

COMMIT;
