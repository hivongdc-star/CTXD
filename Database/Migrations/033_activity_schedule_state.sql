BEGIN;
ALTER TABLE scheduled_activities ADD COLUMN IF NOT EXISTS status SMALLINT NOT NULL DEFAULT 0 CHECK(status BETWEEN 0 AND 2);
ALTER TABLE scheduled_activities ADD COLUMN IF NOT EXISTS activated_at TIMESTAMPTZ;
ALTER TABLE scheduled_activities ADD COLUMN IF NOT EXISTS expired_at TIMESTAMPTZ;
CREATE UNIQUE INDEX IF NOT EXISTS ux_scheduled_activity_open_type ON scheduled_activities(activity_type) WHERE status<2;
COMMIT;
