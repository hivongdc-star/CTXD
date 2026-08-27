BEGIN;

CREATE TABLE IF NOT EXISTS player_courtesy(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  li_yi_du INTEGER NOT NULL DEFAULT 0 CHECK(li_yi_du>=0 AND li_yi_du<=784000),
  reward_info TEXT NULL
);

CREATE TABLE IF NOT EXISTS courtesy_offers(
  source_player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  event_id INTEGER NOT NULL,
  source_key TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS courtesy_events(
  id BIGSERIAL PRIMARY KEY,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  type SMALLINT NOT NULL CHECK(type IN(1,2)),
  counterparty_player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  player_name TEXT NOT NULL,
  player_pic INTEGER NOT NULL,
  player_level INTEGER NOT NULL,
  event_id INTEGER NOT NULL,
  state SMALLINT NOT NULL DEFAULT 1 CHECK(state IN(1,2)),
  source_key TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  handled_at TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_courtesy_event_source
  ON courtesy_events(player_id,source_key) WHERE source_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_courtesy_events_player_order
  ON courtesy_events(player_id,id DESC);
CREATE INDEX IF NOT EXISTS ix_courtesy_offers_event
  ON courtesy_offers(event_id,created_at);

COMMIT;
