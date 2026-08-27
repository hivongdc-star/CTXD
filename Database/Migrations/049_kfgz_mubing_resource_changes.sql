BEGIN;

ALTER TABLE kfgz_deployments
  ADD COLUMN IF NOT EXISTS mubing_active BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS mubing_updated_at TIMESTAMPTZ;

ALTER TABLE kfgz_signups
  ADD COLUMN IF NOT EXISTS resource_version BIGINT NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS kfgz_resource_changes(
  id BIGSERIAL PRIMARY KEY,
  season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  unit TEXT NOT NULL,
  delta BIGINT NOT NULL,
  reason TEXT NOT NULL,
  general_id INTEGER,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_kfgz_resource_changes_player
  ON kfgz_resource_changes(season_id,player_id,id);

COMMIT;
