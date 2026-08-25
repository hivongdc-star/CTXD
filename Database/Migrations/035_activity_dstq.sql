BEGIN;
CREATE TABLE IF NOT EXISTS player_dstq_activity(
 player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
 activity_id BIGINT NOT NULL REFERENCES scheduled_activities(id) ON DELETE CASCADE,
 consume_gold INTEGER NOT NULL DEFAULT 0,
 ticket_106 INTEGER NOT NULL DEFAULT 0,
 ticket_107 INTEGER NOT NULL DEFAULT 0,
 updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS player_dstq_grants(
 player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 activity_id BIGINT NOT NULL REFERENCES scheduled_activities(id) ON DELETE CASCADE,
 threshold_gold INTEGER NOT NULL,
 item_id INTEGER NOT NULL,
 quantity INTEGER NOT NULL,
 created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,activity_id,threshold_gold)
);
COMMIT;
