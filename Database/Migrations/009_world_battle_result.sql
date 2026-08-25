BEGIN;

ALTER TABLE world_battle_handoffs
  ADD COLUMN IF NOT EXISTS winner_force_id SMALLINT NULL,
  ADD COLUMN IF NOT EXISTS result_payload JSONB NULL;

DROP INDEX IF EXISTS ix_world_battle_active_city;
CREATE UNIQUE INDEX IF NOT EXISTS ux_world_battle_active_city
  ON world_battle_handoffs(city_id) WHERE status=0;

COMMIT;
