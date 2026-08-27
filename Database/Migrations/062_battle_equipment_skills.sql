ALTER TABLE battle_units
  ADD COLUMN IF NOT EXISTS equip_att_b integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS equip_def_b integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS equip_tactic_att integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS equip_tactic_def integer NOT NULL DEFAULT 0;
