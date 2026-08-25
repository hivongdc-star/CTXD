CREATE TABLE IF NOT EXISTS kfgz_rounds(
  id BIGSERIAL PRIMARY KEY,season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,
  round_no INTEGER NOT NULL,world_id INTEGER NOT NULL,force1 SMALLINT NOT NULL,force2 SMALLINT NOT NULL,
  state SMALLINT NOT NULL DEFAULT 0,starts_at TIMESTAMPTZ NOT NULL,deadline_at TIMESTAMPTZ NOT NULL,
  winner_side SMALLINT,side1_cities INTEGER,side2_cities INTEGER,resolved_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),UNIQUE(season_id,round_no,force1,force2));
CREATE TABLE IF NOT EXISTS kfgz_city_states(
  round_id BIGINT NOT NULL REFERENCES kfgz_rounds(id) ON DELETE CASCADE,city_id INTEGER NOT NULL,
  owner_side SMALLINT NOT NULL,state SMALLINT NOT NULL DEFAULT 0,updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(round_id,city_id));
CREATE TABLE IF NOT EXISTS kfgz_deployments(
  round_id BIGINT NOT NULL REFERENCES kfgz_rounds(id) ON DELETE CASCADE,player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_id INTEGER NOT NULL,city_id INTEGER NOT NULL,state SMALLINT NOT NULL DEFAULT 1,battle_id BIGINT REFERENCES world_battle_handoffs(id),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),PRIMARY KEY(round_id,player_id,general_id));
CREATE TABLE IF NOT EXISTS kfgz_battles(
  id BIGSERIAL PRIMARY KEY,round_id BIGINT NOT NULL REFERENCES kfgz_rounds(id) ON DELETE CASCADE,city_id INTEGER NOT NULL,
  attacker_player_id BIGINT NOT NULL REFERENCES players(id),defender_player_id BIGINT NOT NULL REFERENCES players(id),
  attacker_side SMALLINT NOT NULL,defender_side SMALLINT NOT NULL,battle_id BIGINT NOT NULL UNIQUE REFERENCES world_battle_handoffs(id),
  state SMALLINT NOT NULL DEFAULT 1,winner_player_id BIGINT REFERENCES players(id),resolved_at TIMESTAMPTZ);
CREATE TABLE IF NOT EXISTS kfgz_player_stats(
  season_id BIGINT NOT NULL REFERENCES kfgz_seasons(id) ON DELETE CASCADE,player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  kill_army BIGINT NOT NULL DEFAULT 0,occupy_city INTEGER NOT NULL DEFAULT 0,solo_wins INTEGER NOT NULL DEFAULT 0,
  wins INTEGER NOT NULL DEFAULT 0,losses INTEGER NOT NULL DEFAULT 0,updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(season_id,player_id));
CREATE TABLE IF NOT EXISTS kfgz_settlement_ledger(
  battle_id BIGINT PRIMARY KEY REFERENCES world_battle_handoffs(id) ON DELETE CASCADE,created_at TIMESTAMPTZ NOT NULL DEFAULT now());
