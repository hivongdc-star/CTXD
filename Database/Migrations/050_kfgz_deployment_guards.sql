BEGIN;

CREATE OR REPLACE FUNCTION kfgz_guard_battle_deployment()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  battle_city INTEGER;
  attacker BIGINT;
  defender BIGINT;
BEGIN
  IF NEW.state = 1 AND NEW.city_id <> OLD.city_id AND EXISTS(
    SELECT 1 FROM kfgz_battles
    WHERE round_id = NEW.round_id AND city_id = NEW.city_id AND state = 1
  ) THEN
    RAISE EXCEPTION 'KFGZ target city % already has an active battle', NEW.city_id;
  END IF;

  IF NEW.state = 3 AND NEW.battle_id IS NOT NULL THEN
    SELECT city_id,attacker_player_id,defender_player_id
      INTO battle_city,attacker,defender
    FROM kfgz_battles
    WHERE battle_id = NEW.battle_id;

    IF battle_city IS NULL THEN
      RAISE EXCEPTION 'KFGZ deployment references unknown battle %', NEW.battle_id;
    END IF;

    IF NEW.player_id = defender AND (OLD.state <> 1 OR OLD.city_id <> battle_city) THEN
      RAISE EXCEPTION 'KFGZ defender moved before battle lock: player %, general %', NEW.player_id, NEW.general_id;
    END IF;

    IF NEW.player_id <> attacker AND NEW.player_id <> defender THEN
      RAISE EXCEPTION 'KFGZ deployment player % is not a participant of battle %', NEW.player_id, NEW.battle_id;
    END IF;

    NEW.city_id := battle_city;
    NEW.mubing_active := FALSE;
    NEW.mubing_updated_at := NULL;
  END IF;
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_kfgz_guard_battle_deployment ON kfgz_deployments;
CREATE TRIGGER trg_kfgz_guard_battle_deployment
BEFORE UPDATE OF state,battle_id,city_id ON kfgz_deployments
FOR EACH ROW
EXECUTE FUNCTION kfgz_guard_battle_deployment();

CREATE OR REPLACE FUNCTION kfgz_sync_signup_gold()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  NEW.sys_gold := COALESCE((SELECT COALESCE(user_gold,0) + COALESCE(sys_gold,0) FROM players WHERE id = NEW.player_id), NEW.sys_gold);
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_kfgz_sync_signup_gold ON kfgz_signups;
CREATE TRIGGER trg_kfgz_sync_signup_gold
BEFORE INSERT OR UPDATE OF sys_gold ON kfgz_signups
FOR EACH ROW
EXECUTE FUNCTION kfgz_sync_signup_gold();

COMMIT;
