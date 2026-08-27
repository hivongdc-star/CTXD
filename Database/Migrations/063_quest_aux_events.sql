CREATE TABLE IF NOT EXISTS player_quest_events(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  kind TEXT NOT NULL,
  arg INTEGER NOT NULL DEFAULT 0,
  count INTEGER NOT NULL DEFAULT 0 CHECK(count>=0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,kind,arg)
);
