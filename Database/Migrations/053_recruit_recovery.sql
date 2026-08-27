BEGIN;

CREATE TABLE IF NOT EXISTS player_recruit_runtime(
  player_id BIGINT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  recruit_token INTEGER NOT NULL DEFAULT 20 CHECK(recruit_token>=0),
  reset_day DATE NOT NULL DEFAULT ((now() AT TIME ZONE 'Asia/Shanghai')::date),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE player_generals
  ADD COLUMN IF NOT EXISTS recruit_updated_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE OR REPLACE FUNCTION ctxd_touch_recruit_time_on_loss()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF NEW.forces < OLD.forces THEN
    NEW.recruit_updated_at := now();
  END IF;
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_player_generals_recruit_loss ON player_generals;
CREATE TRIGGER trg_player_generals_recruit_loss
BEFORE UPDATE OF forces ON player_generals
FOR EACH ROW
EXECUTE FUNCTION ctxd_touch_recruit_time_on_loss();

COMMIT;
