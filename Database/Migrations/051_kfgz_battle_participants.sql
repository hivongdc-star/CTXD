BEGIN;

CREATE TABLE IF NOT EXISTS kfgz_battle_participants(
  battle_id BIGINT NOT NULL REFERENCES kfgz_battles(battle_id) ON DELETE CASCADE,
  player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  side SMALLINT NOT NULL CHECK(side IN (1,2)),
  joined_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(battle_id,player_id)
);
CREATE INDEX IF NOT EXISTS ix_kfgz_battle_participants_player
  ON kfgz_battle_participants(player_id,battle_id);

INSERT INTO kfgz_battle_participants(battle_id,player_id,side)
SELECT battle_id,attacker_player_id,attacker_side FROM kfgz_battles
ON CONFLICT(battle_id,player_id) DO NOTHING;
INSERT INTO kfgz_battle_participants(battle_id,player_id,side)
SELECT battle_id,defender_player_id,defender_side FROM kfgz_battles
ON CONFLICT(battle_id,player_id) DO NOTHING;

CREATE OR REPLACE FUNCTION kfgz_seed_battle_participants()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  INSERT INTO kfgz_battle_participants(battle_id,player_id,side)
  VALUES(NEW.battle_id,NEW.attacker_player_id,NEW.attacker_side)
  ON CONFLICT(battle_id,player_id) DO NOTHING;
  INSERT INTO kfgz_battle_participants(battle_id,player_id,side)
  VALUES(NEW.battle_id,NEW.defender_player_id,NEW.defender_side)
  ON CONFLICT(battle_id,player_id) DO NOTHING;
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_kfgz_seed_battle_participants ON kfgz_battles;
CREATE TRIGGER trg_kfgz_seed_battle_participants
AFTER INSERT ON kfgz_battles
FOR EACH ROW
EXECUTE FUNCTION kfgz_seed_battle_participants();

CREATE OR REPLACE FUNCTION kfgz_guard_battle_deployment()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  battle_city INTEGER;
  original_defender BIGINT;
BEGIN
  IF NEW.state = 1 AND NEW.city_id <> OLD.city_id AND EXISTS(
    SELECT 1 FROM kfgz_battles
    WHERE round_id = NEW.round_id AND city_id = NEW.city_id AND state = 1
  ) THEN
    RAISE EXCEPTION 'KFGZ target city % already has an active battle', NEW.city_id;
  END IF;

  IF NEW.state = 3 AND NEW.battle_id IS NOT NULL THEN
    SELECT city_id,defender_player_id
      INTO battle_city,original_defender
    FROM kfgz_battles
    WHERE battle_id = NEW.battle_id AND state = 1;

    IF battle_city IS NULL THEN
      RAISE EXCEPTION 'KFGZ deployment references unknown active battle %', NEW.battle_id;
    END IF;

    IF NOT EXISTS(
      SELECT 1 FROM kfgz_battle_participants p
      WHERE p.battle_id=NEW.battle_id AND p.player_id=NEW.player_id
    ) THEN
      RAISE EXCEPTION 'KFGZ deployment player % is not a participant of battle %', NEW.player_id, NEW.battle_id;
    END IF;

    IF NEW.player_id = original_defender
       AND NOT EXISTS(SELECT 1 FROM battle_units u WHERE u.battle_id=NEW.battle_id AND u.player_id=NEW.player_id AND u.is_phantom=false)
       AND (OLD.state <> 1 OR OLD.city_id <> battle_city) THEN
      RAISE EXCEPTION 'KFGZ defender moved before battle lock: player %, general %', NEW.player_id, NEW.general_id;
    END IF;

    NEW.city_id := battle_city;
    NEW.mubing_active := FALSE;
    NEW.mubing_updated_at := NULL;
  END IF;
  RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION kfgz_sync_general_after_deployment()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF OLD.state = 3 AND NEW.state = 1 AND OLD.battle_id IS NOT NULL AND NEW.battle_id IS NULL THEN
    UPDATE player_generals
    SET state=1,updated_at=now()
    WHERE player_id=NEW.player_id AND general_id=NEW.general_id;
  END IF;
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_kfgz_sync_general_after_deployment ON kfgz_deployments;
CREATE TRIGGER trg_kfgz_sync_general_after_deployment
AFTER UPDATE OF state,battle_id ON kfgz_deployments
FOR EACH ROW
EXECUTE FUNCTION kfgz_sync_general_after_deployment();

CREATE OR REPLACE FUNCTION kfgz_settle_extra_participants()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  season BIGINT;
  winner_side INTEGER;
  participant RECORD;
  damage BIGINT;
BEGIN
  IF OLD.state <> 2 AND NEW.state = 2 THEN
    SELECT r.season_id,(h.result_payload->>'winnerSide')::int
      INTO season,winner_side
    FROM kfgz_rounds r
    JOIN world_battle_handoffs h ON h.id=NEW.battle_id
    WHERE r.id=NEW.round_id;

    FOR participant IN
      SELECT p.player_id,p.side
      FROM kfgz_battle_participants p
      WHERE p.battle_id=NEW.battle_id
        AND p.player_id NOT IN(NEW.attacker_player_id,NEW.defender_player_id)
    LOOP
      SELECT COALESCE(sum(
        CASE
          WHEN participant.side=1 AND au.player_id=participant.player_id THEN br.defender_damage
          WHEN participant.side=2 AND du.player_id=participant.player_id THEN br.attacker_damage
          ELSE 0
        END),0)::bigint
      INTO damage
      FROM battle_rounds br
      JOIN battle_units au ON au.id=br.attacker_unit_id
      JOIN battle_units du ON du.id=br.defender_unit_id
      WHERE br.battle_id=NEW.battle_id;

      INSERT INTO kfgz_player_stats(season_id,player_id,kill_army,wins,losses)
      VALUES(season,participant.player_id,damage,
             CASE WHEN participant.side=winner_side THEN 1 ELSE 0 END,
             CASE WHEN participant.side=winner_side THEN 0 ELSE 1 END)
      ON CONFLICT(season_id,player_id) DO UPDATE SET
        kill_army=kfgz_player_stats.kill_army+excluded.kill_army,
        wins=kfgz_player_stats.wins+excluded.wins,
        losses=kfgz_player_stats.losses+excluded.losses,
        updated_at=now();
    END LOOP;
  END IF;
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_kfgz_settle_extra_participants ON kfgz_battles;
CREATE TRIGGER trg_kfgz_settle_extra_participants
AFTER UPDATE OF state ON kfgz_battles
FOR EACH ROW
EXECUTE FUNCTION kfgz_settle_extra_participants();

COMMIT;
