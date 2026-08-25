BEGIN;

CREATE TABLE IF NOT EXISTS battles(
  id BIGINT PRIMARY KEY REFERENCES world_battle_handoffs(id) ON DELETE CASCADE,
  city_id INTEGER NOT NULL,
  status SMALLINT NOT NULL DEFAULT 0,
  round_no INTEGER NOT NULL DEFAULT 0,
  winner_side SMALLINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  resolved_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS battle_units(
  id BIGSERIAL PRIMARY KEY,
  battle_id BIGINT NOT NULL REFERENCES battles(id) ON DELETE CASCADE,
  side SMALLINT NOT NULL CHECK(side IN (1,2)),
  sequence INTEGER NOT NULL,
  player_id BIGINT NULL REFERENCES players(id) ON DELETE SET NULL,
  general_id INTEGER NOT NULL,
  troop_id INTEGER NOT NULL,
  name TEXT NOT NULL,
  level INTEGER NOT NULL,
  attack INTEGER NOT NULL,
  defense INTEGER NOT NULL,
  leader INTEGER NOT NULL,
  strength INTEGER NOT NULL,
  hp INTEGER NOT NULL,
  max_hp INTEGER NOT NULL,
  is_npc BOOLEAN NOT NULL DEFAULT FALSE,
  UNIQUE(battle_id,side,sequence)
);

CREATE TABLE IF NOT EXISTS battle_rounds(
  id BIGSERIAL PRIMARY KEY,
  battle_id BIGINT NOT NULL REFERENCES battles(id) ON DELETE CASCADE,
  round_no INTEGER NOT NULL,
  attacker_unit_id BIGINT NOT NULL REFERENCES battle_units(id),
  defender_unit_id BIGINT NOT NULL REFERENCES battle_units(id),
  attacker_damage INTEGER NOT NULL,
  defender_damage INTEGER NOT NULL,
  attacker_hp INTEGER NOT NULL,
  defender_hp INTEGER NOT NULL,
  winner_side SMALLINT NOT NULL,
  report JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE(battle_id,round_no)
);

COMMIT;
