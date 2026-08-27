BEGIN;

-- Migration 046 originally introduced player_tickets(balance), while later ticket
-- services use player_tickets(tickets). Keep existing rows and normalize the
-- canonical column without touching Feast behavior.
ALTER TABLE player_tickets ADD COLUMN IF NOT EXISTS tickets BIGINT;
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='player_tickets' AND column_name='balance'
    ) THEN
        EXECUTE 'UPDATE player_tickets SET tickets=COALESCE(tickets,balance,0) WHERE tickets IS NULL';
    ELSE
        UPDATE player_tickets SET tickets=0 WHERE tickets IS NULL;
    END IF;
END $$;
ALTER TABLE player_tickets ALTER COLUMN tickets SET DEFAULT 0;
ALTER TABLE player_tickets ALTER COLUMN tickets SET NOT NULL;

-- Mail rows themselves are idempotent through player_mail.source_key. This
-- ledger prevents the KFZB maintenance worker from repeatedly emitting the same
-- notification/push after reconnects or scheduler retries.
CREATE TABLE IF NOT EXISTS kfzb_reward_notice_ledger(
    season_id BIGINT NOT NULL REFERENCES kfzb_seasons(id) ON DELETE CASCADE,
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    kind TEXT NOT NULL CHECK(kind IN('eliminated','title','end')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(season_id,player_id,kind)
);

-- Legacy title timing: runner-up/top-4/top-8/qualifier titles are attached when
-- elimination is finalized. The champion title must not become active before
-- the event reaches the terminal state (legacy globalState >= 70 check).
CREATE OR REPLACE FUNCTION kfzb_enforce_champion_title_timing()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    state_value SMALLINT;
BEGIN
    IF NEW.eliminated_layer=0 AND NEW.title='天下第一擂主' THEN
        SELECT global_state INTO state_value FROM kfzb_seasons WHERE id=NEW.season_id;
        IF COALESCE(state_value,0)<70 THEN
            NEW.title=NULL;
        END IF;
    END IF;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_kfzb_champion_title_timing ON kfzb_rewards;
CREATE TRIGGER trg_kfzb_champion_title_timing
BEFORE INSERT OR UPDATE OF title,eliminated_layer ON kfzb_rewards
FOR EACH ROW EXECUTE FUNCTION kfzb_enforce_champion_title_timing();

COMMIT;
