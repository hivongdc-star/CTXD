CREATE TABLE IF NOT EXISTS kfwd_seasons(
  id BIGSERIAL PRIMARY KEY, season_no INTEGER NOT NULL UNIQUE, global_state SMALLINT NOT NULL DEFAULT 20,
  signup_opens_at TIMESTAMPTZ NOT NULL, sync_opens_at TIMESTAMPTZ NOT NULL,
  battle_opens_at TIMESTAMPTZ NOT NULL, ends_at TIMESTAMPTZ NOT NULL,
  min_level INTEGER NOT NULL, round_interval_seconds INTEGER NOT NULL, total_rounds INTEGER NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(), updated_at TIMESTAMPTZ NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS kfwd_schedules(
  season_id BIGINT NOT NULL REFERENCES kfwd_seasons(id) ON DELETE CASCADE, schedule_id INTEGER NOT NULL,
  min_level INTEGER NOT NULL, max_level_exclusive INTEGER NOT NULL, PRIMARY KEY(season_id,schedule_id));
CREATE TABLE IF NOT EXISTS kfwd_signups(
  season_id BIGINT NOT NULL REFERENCES kfwd_seasons(id) ON DELETE CASCADE, player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  schedule_id INTEGER NOT NULL, group_type INTEGER NOT NULL DEFAULT 0, general_ids INTEGER[] NOT NULL,
  synced BOOLEAN NOT NULL DEFAULT false, state SMALLINT NOT NULL DEFAULT 0, competitor_id BIGSERIAL,
  version INTEGER NOT NULL DEFAULT 0, signed_at TIMESTAMPTZ NOT NULL DEFAULT now(), updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(season_id,player_id), UNIQUE(season_id,competitor_id));
CREATE TABLE IF NOT EXISTS kfwd_matches(
  id BIGSERIAL PRIMARY KEY, season_id BIGINT NOT NULL REFERENCES kfwd_seasons(id) ON DELETE CASCADE,
  schedule_id INTEGER NOT NULL, round_no INTEGER NOT NULL, player1_id BIGINT NOT NULL REFERENCES players(id), player2_id BIGINT REFERENCES players(id),
  battle_id BIGINT REFERENCES world_battle_handoffs(id), state SMALLINT NOT NULL DEFAULT 0,
  winner_player_id BIGINT REFERENCES players(id), starts_at TIMESTAMPTZ NOT NULL, deadline_at TIMESTAMPTZ NOT NULL,
  resolved_at TIMESTAMPTZ, created_at TIMESTAMPTZ NOT NULL DEFAULT now(), UNIQUE(season_id,round_no,player1_id));
CREATE INDEX IF NOT EXISTS ix_kfwd_match_player2 ON kfwd_matches(season_id,round_no,player2_id);
CREATE TABLE IF NOT EXISTS kfwd_rewards(
  season_id BIGINT NOT NULL REFERENCES kfwd_seasons(id) ON DELETE CASCADE, player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  wins INTEGER NOT NULL DEFAULT 0, losses INTEGER NOT NULL DEFAULT 0, tickets INTEGER NOT NULL DEFAULT 0,
  reward_info JSONB NOT NULL DEFAULT '{}'::jsonb, day_ranking INTEGER, day_reward_claimed BOOLEAN NOT NULL DEFAULT false,
  treasure_claimed BOOLEAN NOT NULL DEFAULT false, updated_at TIMESTAMPTZ NOT NULL DEFAULT now(), PRIMARY KEY(season_id,player_id));
