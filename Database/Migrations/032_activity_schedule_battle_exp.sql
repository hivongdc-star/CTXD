BEGIN;
CREATE TABLE IF NOT EXISTS scheduled_activities(
 id BIGSERIAL PRIMARY KEY,activity_type INTEGER NOT NULL,start_at TIMESTAMPTZ NOT NULL,end_at TIMESTAMPTZ NOT NULL,
 params_info TEXT NOT NULL DEFAULT '',status SMALLINT NOT NULL DEFAULT 0 CHECK(status BETWEEN 0 AND 2),
 activated_at TIMESTAMPTZ,expired_at TIMESTAMPTZ,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),CHECK(end_at>start_at)
);
CREATE INDEX IF NOT EXISTS ix_scheduled_activities_active ON scheduled_activities(activity_type,start_at,end_at);
CREATE TABLE IF NOT EXISTS player_battle_exp_activity(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,activity_id BIGINT NOT NULL REFERENCES scheduled_activities(id) ON DELETE CASCADE,
 active_day DATE NOT NULL,activated_at TIMESTAMPTZ NOT NULL DEFAULT now(),PRIMARY KEY(player_id,activity_id,active_day)
);
CREATE TABLE IF NOT EXISTS player_level_exp_activity(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,activity_id BIGINT NOT NULL REFERENCES scheduled_activities(id) ON DELETE CASCADE,
 start_level INTEGER NOT NULL,start_exp BIGINT NOT NULL,claimed BOOLEAN NOT NULL DEFAULT FALSE,
 joined_at TIMESTAMPTZ NOT NULL DEFAULT now(),claimed_at TIMESTAMPTZ,PRIMARY KEY(player_id,activity_id)
);
COMMIT;
