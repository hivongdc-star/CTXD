ALTER TABLE kfzb_spectator_state ALTER COLUMN free_feast_cards SET DEFAULT 10;
ALTER TABLE kfzb_spectator_state ADD COLUMN IF NOT EXISTS gold_feast_cards INTEGER NOT NULL DEFAULT 0;
ALTER TABLE kfzb_spectator_state ADD COLUMN IF NOT EXISTS feast_cards_bought INTEGER NOT NULL DEFAULT 0;
ALTER TABLE kfzb_spectator_state ADD COLUMN IF NOT EXISTS drink_num INTEGER NOT NULL DEFAULT 0;
UPDATE kfzb_spectator_state SET free_feast_cards=10 WHERE free_feast_cards=0;
