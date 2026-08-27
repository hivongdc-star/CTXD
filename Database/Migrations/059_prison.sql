BEGIN;
CREATE TABLE IF NOT EXISTS player_prisons(
    player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
    prison_lv SMALLINT NOT NULL DEFAULT 1 CHECK(prison_lv BETWEEN 1 AND 5),
    lash_lv SMALLINT NOT NULL DEFAULT 1 CHECK(lash_lv BETWEEN 1 AND 5),
    grab_num INTEGER NOT NULL DEFAULT 0 CHECK(grab_num>=0),
    lash_num INTEGER NOT NULL DEFAULT 0 CHECK(lash_num>=0),
    auto_lash_exp BIGINT NOT NULL DEFAULT 0 CHECK(auto_lash_exp>=0),
    point INTEGER NOT NULL DEFAULT 0 CHECK(point>=0),
    expire_at TIMESTAMPTZ NULL,
    trail_gold INTEGER NOT NULL DEFAULT 0 CHECK(trail_gold>=0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS player_slaves(
    id BIGSERIAL PRIMARY KEY,
    holder_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    slave_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    general_id INTEGER NOT NULL,
    grab_time TIMESTAMPTZ NOT NULL DEFAULT now(),
    escape_at TIMESTAMPTZ NULL,
    slash_times INTEGER NOT NULL DEFAULT 0 CHECK(slash_times>=0),
    type SMALLINT NOT NULL DEFAULT 1,
    force_id SMALLINT NOT NULL DEFAULT 0,
    name TEXT NOT NULL DEFAULT '',
    level INTEGER NOT NULL DEFAULT 0 CHECK(level>=0)
);
CREATE INDEX IF NOT EXISTS ix_player_slaves_holder ON player_slaves(holder_player_id,grab_time);
CREATE INDEX IF NOT EXISTS ix_player_slaves_captive ON player_slaves(slave_player_id,general_id);
CREATE INDEX IF NOT EXISTS ix_player_slaves_escape ON player_slaves(escape_at) WHERE escape_at IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_player_slaves_real_general ON player_slaves(slave_player_id,general_id) WHERE type=1;

CREATE TABLE IF NOT EXISTS prison_capture_attempts(
    battle_id BIGINT NOT NULL,
    killed_unit_id BIGINT NOT NULL,
    holder_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    slave_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    general_id INTEGER NOT NULL,
    kill_general INTEGER NOT NULL DEFAULT 0 CHECK(kill_general>=0),
    probability DOUBLE PRECISION NOT NULL DEFAULT 0,
    captured BOOLEAN NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    processed_at TIMESTAMPTZ NULL,
    PRIMARY KEY(battle_id,killed_unit_id)
);
CREATE INDEX IF NOT EXISTS ix_prison_capture_pending ON prison_capture_attempts(created_at) WHERE processed_at IS NULL;
COMMIT;
