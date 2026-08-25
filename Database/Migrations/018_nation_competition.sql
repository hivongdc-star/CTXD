BEGIN;
ALTER TABLE nation_scheduled_tasks ADD COLUMN IF NOT EXISTS initial_owner_force_id SMALLINT NULL;
COMMIT;
