BEGIN;

DO $$
BEGIN
    IF EXISTS(
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='player_tickets' AND column_name='balance'
    ) AND NOT EXISTS(
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='player_tickets' AND column_name='tickets'
    ) THEN
        ALTER TABLE player_tickets RENAME COLUMN balance TO tickets;
    END IF;
END $$;

ALTER TABLE player_tickets
    ADD COLUMN IF NOT EXISTS tickets bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS no_tips boolean NOT NULL DEFAULT false;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_player_tickets_nonnegative') THEN
        ALTER TABLE player_tickets
            ADD CONSTRAINT ck_player_tickets_nonnegative CHECK(tickets>=0);
    END IF;
END $$;

ALTER TABLE player_quest_branches
    ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS claimed_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_player_quest_branches_claimable
    ON player_quest_branches(player_id,branch_id)
    WHERE claimed_at IS NULL;

COMMIT;
