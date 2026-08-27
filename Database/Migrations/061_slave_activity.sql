BEGIN;

CREATE TABLE IF NOT EXISTS player_slave_activity(
    activity_id bigint NOT NULL REFERENCES scheduled_activities(id) ON DELETE CASCADE,
    player_id bigint NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    unlocked_bits integer NOT NULL DEFAULT 0 CHECK(unlocked_bits BETWEEN 0 AND 15),
    captured_bits integer NOT NULL DEFAULT 0 CHECK(captured_bits BETWEEN 0 AND 15),
    lashed_bits integer NOT NULL DEFAULT 0 CHECK(lashed_bits BETWEEN 0 AND 15),
    settled_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(activity_id,player_id)
);

COMMIT;
