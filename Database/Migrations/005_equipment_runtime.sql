BEGIN;

-- Legacy PlayerItemRefresh.refreshAttribute and StoreHouse.refreshAttribute are strings
-- (e.g. semicolon-separated skillId:level pairs), not integers.
ALTER TABLE player_store_offers
  ALTER COLUMN refresh_attribute TYPE TEXT USING refresh_attribute::text,
  ALTER COLUMN refresh_attribute SET DEFAULT '';

ALTER TABLE player_equipment
  ALTER COLUMN refresh_attribute TYPE TEXT USING refresh_attribute::text,
  ALTER COLUMN refresh_attribute SET DEFAULT '';

COMMIT;
