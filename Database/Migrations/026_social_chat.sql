BEGIN;
CREATE TABLE IF NOT EXISTS player_blacklist(
 id BIGSERIAL PRIMARY KEY,player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 blocked_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(player_id,blocked_player_id)
);
CREATE TABLE IF NOT EXISTS chat_messages(
 id BIGSERIAL PRIMARY KEY,sender_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 recipient_player_id BIGINT NULL REFERENCES players(id) ON DELETE CASCADE,force_id SMALLINT NULL,
 channel VARCHAR(16) NOT NULL,message VARCHAR(150) NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_chat_country ON chat_messages(force_id,created_at DESC) WHERE channel='COUNTRY';
CREATE INDEX IF NOT EXISTS ix_chat_private ON chat_messages(recipient_player_id,created_at DESC) WHERE channel='ONE2ONE';
COMMIT;
