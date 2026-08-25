BEGIN;
CREATE TABLE IF NOT EXISTS player_mail(
 id BIGSERIAL PRIMARY KEY,
 sender_player_id BIGINT NULL REFERENCES players(id) ON DELETE SET NULL,
 recipient_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
 sender_name TEXT NOT NULL DEFAULT 'System', title TEXT NOT NULL, body TEXT NOT NULL,
 mail_type SMALLINT NOT NULL DEFAULT 1, link_id BIGINT NOT NULL DEFAULT 0,
 is_read BOOLEAN NOT NULL DEFAULT false, is_deleted BOOLEAN NOT NULL DEFAULT false,
 is_saved BOOLEAN NOT NULL DEFAULT false, attachments JSONB NOT NULL DEFAULT '[]'::jsonb,
 source_key TEXT NULL,
 attachments_claimed_at TIMESTAMPTZ NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_player_mail_inbox ON player_mail(recipient_player_id,is_deleted,created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_player_mail_source ON player_mail(recipient_player_id,source_key) WHERE source_key IS NOT NULL;
COMMIT;
