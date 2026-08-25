BEGIN;
CREATE TABLE IF NOT EXISTS nation_forces(
  force_id SMALLINT PRIMARY KEY CHECK(force_id BETWEEN 1 AND 3),
  level INTEGER NOT NULL DEFAULT 1 CHECK(level BETWEEN 1 AND 7),
  exp BIGINT NOT NULL DEFAULT 0,
  stage SMALLINT NOT NULL DEFAULT 4,
  trial_ends_at TIMESTAMPTZ NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
INSERT INTO nation_forces(force_id) VALUES(1),(2),(3) ON CONFLICT DO NOTHING;
ALTER TABLE players ADD COLUMN IF NOT EXISTS official_id INTEGER NOT NULL DEFAULT 13;
ALTER TABLE players ADD COLUMN IF NOT EXISTS salary_claimed_on DATE NULL;
CREATE TABLE IF NOT EXISTS player_civil_affairs(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  general_id INTEGER NOT NULL,
  affair_id INTEGER NOT NULL,
  level INTEGER NOT NULL DEFAULT 1,
  started_at TIMESTAMPTZ NULL,
  PRIMARY KEY(player_id,general_id,affair_id),
  FOREIGN KEY(player_id,general_id) REFERENCES player_generals(player_id,general_id) ON DELETE CASCADE
);
ALTER TABLE player_buildings ADD COLUMN IF NOT EXISTS politics_event_id INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS player_politics(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  event_count INTEGER NOT NULL DEFAULT 0 CHECK(event_count BETWEEN 0 AND 24),
  people_loyal INTEGER NOT NULL DEFAULT 0 CHECK(people_loyal BETWEEN 0 AND 100),
  last_event_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS nation_office_buildings(
  force_id SMALLINT NOT NULL CHECK(force_id BETWEEN 1 AND 3),
  building_id INTEGER NOT NULL,
  owner_player_id BIGINT NOT NULL REFERENCES players(id),
  member_count INTEGER NOT NULL DEFAULT 1 CHECK(member_count BETWEEN 1 AND 3),
  auto_pass BOOLEAN NOT NULL DEFAULT FALSE,
  occupied_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(force_id,building_id)
);
CREATE TABLE IF NOT EXISTS player_office_memberships(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  force_id SMALLINT NOT NULL,
  building_id INTEGER NOT NULL,
  is_leader BOOLEAN NOT NULL DEFAULT FALSE,
  state SMALLINT NOT NULL DEFAULT 0 CHECK(state IN(0,1)),
  is_new BOOLEAN NOT NULL DEFAULT FALSE
);
COMMIT;
