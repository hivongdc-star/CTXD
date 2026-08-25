CREATE TABLE IF NOT EXISTS battle_troop_drop_grants(
  battle_id BIGINT NOT NULL REFERENCES battles(id) ON DELETE CASCADE,
  killed_unit_id BIGINT NOT NULL REFERENCES battle_units(id) ON DELETE CASCADE,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  drop_type INTEGER NOT NULL,
  amount INTEGER NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(battle_id,killed_unit_id,player_id,drop_type)
);
