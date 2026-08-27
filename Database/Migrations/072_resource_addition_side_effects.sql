BEGIN;

CREATE TABLE IF NOT EXISTS player_resource_addition_side_effects(
  player_id BIGINT NOT NULL,
  request_key VARCHAR(128) NOT NULL,
  event_activity_id BIGINT NOT NULL REFERENCES scheduled_activities(id) ON DELETE RESTRICT,
  reward_type SMALLINT NOT NULL CHECK(reward_type>0),
  reward_item_id INTEGER NOT NULL,
  reward_quantity INTEGER NOT NULL CHECK(reward_quantity>0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(player_id,request_key),
  FOREIGN KEY(player_id,request_key)
    REFERENCES player_resource_addition_requests(player_id,request_key)
    ON DELETE CASCADE
);

COMMIT;
