BEGIN;
CREATE TABLE IF NOT EXISTS player_weapons(
    player_id bigint NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    weapon_id integer NOT NULL,
    level integer NOT NULL DEFAULT 0 CHECK(level>=0),
    gem_id integer NOT NULL DEFAULT 0,
    times integer NOT NULL DEFAULT 0 CHECK(times>=0),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(player_id,weapon_id)
);

ALTER TABLE player_quest_runtime
    ADD COLUMN IF NOT EXISTS arms_weapon_views integer NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS player_quest_branches(
    player_id bigint NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    branch_id integer NOT NULL,
    unlocked_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(player_id,branch_id)
);

CREATE TABLE IF NOT EXISTS player_incense_unlocks(
    player_id bigint NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    incense_id integer NOT NULL,
    unlocked_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(player_id,incense_id)
);
COMMIT;
