BEGIN;

-- Legacy StoreHouse type=3 vertical slice for General Treasure. KFZB grants fixed
-- lea/str values that may exceed the normal static treasure roll ranges, so the
-- rolled attributes are persisted on each instance rather than derived later.
CREATE TABLE IF NOT EXISTS player_general_treasures(
    id BIGSERIAL PRIMARY KEY,
    player_id BIGINT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    treasure_id INTEGER NOT NULL CHECK(treasure_id>0),
    lea INTEGER NOT NULL CHECK(lea>=0),
    str INTEGER NOT NULL CHECK(str>=0),
    owner_general_id INTEGER NOT NULL DEFAULT 0,
    state SMALLINT NOT NULL DEFAULT 0 CHECK(state IN(0,1)),
    source TEXT NOT NULL,
    source_key TEXT NOT NULL,
    acquired_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(player_id,source_key),
    CHECK((state=0 AND owner_general_id=0) OR (state=1 AND owner_general_id>0))
);

CREATE INDEX IF NOT EXISTS ix_player_general_treasures_player
    ON player_general_treasures(player_id,id);
CREATE INDEX IF NOT EXISTS ix_player_general_treasures_owner
    ON player_general_treasures(player_id,owner_general_id)
    WHERE state=1;

COMMIT;
