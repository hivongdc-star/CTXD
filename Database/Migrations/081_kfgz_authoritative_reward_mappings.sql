BEGIN;

CREATE TABLE IF NOT EXISTS kfgz_battle_reward(
    id INTEGER PRIMARY KEY,
    kill_rank_reward_info TEXT NOT NULL,
    solo_reward INTEGER NOT NULL CHECK(solo_reward>=0),
    occupy_city_reward INTEGER NOT NULL CHECK(occupy_city_reward>=0),
    double_info TEXT NULL,
    city_reward TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS kfgz_end_reward(
    id INTEGER PRIMARY KEY,
    reward_info TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS kfgz_reward(
    id INTEGER PRIMARY KEY,
    group_id INTEGER NOT NULL,
    layer_id INTEGER NOT NULL,
    battle_reward_id INTEGER NOT NULL REFERENCES kfgz_battle_reward(id),
    end_reward_id INTEGER NOT NULL REFERENCES kfgz_end_reward(id),
    UNIQUE(group_id,layer_id)
);

INSERT INTO kfgz_battle_reward(id,kill_rank_reward_info,solo_reward,occupy_city_reward,double_info,city_reward) VALUES
(1,'5:2000,10:1200,20:700,30:400,99999:200',5,10,NULL,'cnum:0,win:1200,lost:400'),
(2,'5:3000,10:2400,20:1000,30:600,99999:300',10,20,NULL,'cnum:0,win:1500,lost:750')
ON CONFLICT(id) DO UPDATE SET
    kill_rank_reward_info=EXCLUDED.kill_rank_reward_info,
    solo_reward=EXCLUDED.solo_reward,
    occupy_city_reward=EXCLUDED.occupy_city_reward,
    double_info=EXCLUDED.double_info,
    city_reward=EXCLUDED.city_reward;

INSERT INTO kfgz_end_reward(id,reward_info) VALUES
(1,'15:1000,40:2000,60:4000,75:8000'),
(2,'25:2000,60:4000,85:6000,100:8000')
ON CONFLICT(id) DO UPDATE SET reward_info=EXCLUDED.reward_info;

INSERT INTO kfgz_reward(id,group_id,layer_id,battle_reward_id,end_reward_id) VALUES
(1,1,1,1,1),
(2,1,2,2,2)
ON CONFLICT(id) DO UPDATE SET
    group_id=EXCLUDED.group_id,
    layer_id=EXCLUDED.layer_id,
    battle_reward_id=EXCLUDED.battle_reward_id,
    end_reward_id=EXCLUDED.end_reward_id;

COMMIT;
