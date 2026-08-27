BEGIN;

CREATE TABLE IF NOT EXISTS player_resource_addition_requests(
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  request_key VARCHAR(128) NOT NULL,
  source VARCHAR(8) NOT NULL CHECK(source IN ('paid','item')),
  resource_type SMALLINT NOT NULL CHECK(resource_type BETWEEN 1 AND 5),
  addition_mode SMALLINT NOT NULL CHECK(addition_mode BETWEEN 1 AND 3),
  time_type SMALLINT NOT NULL CHECK(time_type BETWEEN 1 AND 3),
  charge_item_id INTEGER NULL,
  item_id INTEGER NULL,
  gold_spent BIGINT NOT NULL DEFAULT 0 CHECK(gold_spent>=0),
  ends_at TIMESTAMPTZ NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,request_key)
);

COMMIT;
