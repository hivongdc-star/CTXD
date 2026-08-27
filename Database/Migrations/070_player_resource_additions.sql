BEGIN;

CREATE TABLE IF NOT EXISTS player_resource_additions(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  resource_type SMALLINT NOT NULL CHECK(resource_type BETWEEN 1 AND 5),
  addition_mode SMALLINT NOT NULL CHECK(addition_mode BETWEEN 1 AND 3),
  time_type SMALLINT NOT NULL CHECK(time_type BETWEEN 1 AND 3),
  ends_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY(player_id,resource_type)
);

COMMIT;
