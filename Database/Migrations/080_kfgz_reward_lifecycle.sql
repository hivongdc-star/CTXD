BEGIN;
CREATE TABLE IF NOT EXISTS kfgz_round_rewards(
    round_id BIGINT NOT NULL REFERENCES kfgz_rounds(id) ON DELETE CASCADE,
    season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    reward_info TEXT NOT NULL,
    claim_times SMALLINT NOT NULL DEFAULT 0 CHECK(claim_times BETWEEN 0 AND 4),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(round_id,player_id)
);
CREATE INDEX IF NOT EXISTS ix_kfgz_round_rewards_season ON kfgz_round_rewards(season_id,claim_times);

CREATE TABLE IF NOT EXISTS kfgz_end_reward_profiles(
    season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
    force_id SMALLINT NOT NULL CHECK(force_id BETWEEN 1 AND 3),
    reward_info TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(season_id,force_id)
);
CREATE TABLE IF NOT EXISTS kfgz_final_rewards(
    season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    force_id SMALLINT NOT NULL CHECK(force_id BETWEEN 1 AND 3),
    nation_score INTEGER NOT NULL CHECK(nation_score>=0),
    slot1_times SMALLINT NOT NULL DEFAULT 0 CHECK(slot1_times BETWEEN 0 AND 4),
    slot2_times SMALLINT NOT NULL DEFAULT 0 CHECK(slot2_times BETWEEN 0 AND 4),
    slot3_times SMALLINT NOT NULL DEFAULT 0 CHECK(slot3_times BETWEEN 0 AND 4),
    slot4_times SMALLINT NOT NULL DEFAULT 0 CHECK(slot4_times BETWEEN 0 AND 4),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(season_id,player_id)
);
CREATE TABLE IF NOT EXISTS kfgz_title_candidates(
    season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
    force_id SMALLINT NOT NULL CHECK(force_id BETWEEN 1 AND 3),
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    title_key TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(season_id,force_id)
);
CREATE TABLE IF NOT EXISTS kfgz_titles(
    season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
    force_id SMALLINT NOT NULL CHECK(force_id BETWEEN 1 AND 3),
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    player_name TEXT NOT NULL,
    title_key TEXT NOT NULL,
    issued_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(season_id,force_id)
);
CREATE INDEX IF NOT EXISTS ix_kfgz_titles_player ON kfgz_titles(player_id,season_id DESC);
CREATE TABLE IF NOT EXISTS kfgz_reward_ledger(
    season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    reward_kind TEXT NOT NULL,
    reward_ref BIGINT NOT NULL,
    claim_no SMALLINT NOT NULL,
    tickets BIGINT NOT NULL CHECK(tickets>=0),
    gold_cost BIGINT NOT NULL CHECK(gold_cost>=0),
    auto_issue BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(season_id,player_id,reward_kind,reward_ref,claim_no)
);
COMMIT;
