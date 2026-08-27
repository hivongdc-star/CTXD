using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfgzReinforcementRequest(int[] GeneralIds);
public sealed record KfgzReinforcementResult(long BattleId,int CityId,int Side,int[] GeneralIds);

public sealed class KfgzReinforcementService(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technologies,
    BattleService battles,
    GamePushHub push)
{
    public async Task<KfgzReinforcementResult> ReinforceAsync(long playerId,long battleId,KfgzReinforcementRequest request,CancellationToken ct)
    {
        var ids=(request.GeneralIds??[]).Distinct().ToArray();
        if(ids.Length==0)throw new GameException("KFGZ_REINFORCE_GENERALS_REQUIRED","Select at least one general to reinforce the battle.");

        long leadPlayer;
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT attacker_player_id FROM kfgz_battles WHERE battle_id=$1 AND state=1",c))
        {
            q.Parameters.AddWithValue(battleId);
            var value=await q.ExecuteScalarAsync(ct);
            if(value is null)throw new GameException("KFGZ_REINFORCE_BATTLE_INVALID","Target KFGZ battle is not active.",404);
            leadPlayer=Convert.ToInt64(value);
        }

        // KFGZ battle engine is lazily materialized. Initialize it with the original lead player,
        // who is already authorized by the handoff, before adding later camp participants.
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT 1 FROM battles WHERE id=$1",c))
        {
            q.Parameters.AddWithValue(battleId);
            if(await q.ExecuteScalarAsync(ct)is null)await battles.GetAsync(leadPlayer,battleId,ct);
        }

        int cityId,worldId,side;
        DateTimeOffset startsAt;
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            long roundId;
            await using(var meta=new NpgsqlCommand(@"
SELECT kb.round_id,r.world_id,r.starts_at,kb.city_id,
       CASE WHEN s.force_id=r.force1 THEN 1 WHEN s.force_id=r.force2 THEN 2 ELSE 0 END
FROM kfgz_battles kb
JOIN kfgz_rounds r ON r.id=kb.round_id
JOIN kfgz_signups s ON s.season_id=r.season_id AND s.player_id=$2
JOIN battles b ON b.id=kb.battle_id
WHERE kb.battle_id=$1 AND kb.state=1 AND r.state=1 AND b.status=0
FOR UPDATE OF kb,r,b",c,t))
            {
                meta.Parameters.AddWithValue(battleId);meta.Parameters.AddWithValue(playerId);
                await using var r=await meta.ExecuteReaderAsync(ct);
                if(!await r.ReadAsync(ct))throw new GameException("KFGZ_REINFORCE_BATTLE_INVALID","Target KFGZ battle is not active for this player.",409);
                roundId=r.GetInt64(0);worldId=r.GetInt32(1);startsAt=r.GetFieldValue<DateTimeOffset>(2);cityId=r.GetInt32(3);side=r.GetInt32(4);
            }
            if(side is not(1 or 2))throw new GameException("KFGZ_REINFORCE_SIDE_INVALID","Player is not assigned to either side of this KFGZ round.",403);

            var deployments=new List<(int general,int city,int state)>();
            await using(var q=new NpgsqlCommand(@"
SELECT general_id,city_id,state
FROM kfgz_deployments
WHERE round_id=$1 AND player_id=$2 AND general_id=ANY($3)
ORDER BY general_id
FOR UPDATE",c,t))
            {
                q.Parameters.AddWithValue(roundId);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(ids);
                await using var r=await q.ExecuteReaderAsync(ct);
                while(await r.ReadAsync(ct))deployments.Add((r.GetInt32(0),r.GetInt32(1),r.GetInt16(2)));
            }
            if(deployments.Count!=ids.Length||deployments.Any(x=>x.state!=1))
                throw new GameException("KFGZ_REINFORCE_GENERAL_INVALID","Every reinforcing general must be idle in the active KFGZ round.",409);

            foreach(var d in deployments)
            {
                if(d.city==cityId)continue;
                var road=content.KfgzWorldRoads.Values.FirstOrDefault(v=>v.World==worldId&&(v.From==d.city&&v.To==cityId||v.From==cityId&&v.To==d.city));
                if(road is null||!RoadOpen(road,startsAt))
                    throw new GameException("KFGZ_REINFORCE_NOT_ADJACENT","Every reinforcing general must be in the battle city or an adjacent city connected by an open KFGZ road.",409);
            }

            await using(var participant=new NpgsqlCommand(@"
INSERT INTO kfgz_battle_participants(battle_id,player_id,side)
VALUES($1,$2,$3)
ON CONFLICT(battle_id,player_id) DO NOTHING",c,t))
            {participant.Parameters.AddWithValue(battleId);participant.Parameters.AddWithValue(playerId);participant.Parameters.AddWithValue(side);await participant.ExecuteNonQueryAsync(ct);}
            await using(var checkSide=new NpgsqlCommand("SELECT side FROM kfgz_battle_participants WHERE battle_id=$1 AND player_id=$2 FOR UPDATE",c,t))
            {checkSide.Parameters.AddWithValue(battleId);checkSide.Parameters.AddWithValue(playerId);if(Convert.ToInt32(await checkSide.ExecuteScalarAsync(ct))!=side)throw new GameException("KFGZ_REINFORCE_SIDE_CONFLICT","Existing KFGZ battle participant belongs to another side.",409);}

            int sequence;
            await using(var seq=new NpgsqlCommand("SELECT COALESCE(max(sequence),-1)+1 FROM battle_units WHERE battle_id=$1 AND side=$2",c,t))
            {seq.Parameters.AddWithValue(battleId);seq.Parameters.AddWithValue(side);sequence=Convert.ToInt32(await seq.ExecuteScalarAsync(ct));}

            foreach(var generalId in ids)
            {
                await using(var duplicate=new NpgsqlCommand("SELECT 1 FROM battle_units WHERE battle_id=$1 AND player_id=$2 AND general_id=$3 AND is_phantom=false",c,t))
                {duplicate.Parameters.AddWithValue(battleId);duplicate.Parameters.AddWithValue(playerId);duplicate.Parameters.AddWithValue(generalId);if(await duplicate.ExecuteScalarAsync(ct)is not null)throw new GameException("KFGZ_REINFORCE_DUPLICATE","General already participates in this KFGZ battle.",409);}
                await AddPlayerUnitAsync(c,t,battleId,side,sequence++,playerId,generalId,cityId,ct);
            }

            await using(var deploy=new NpgsqlCommand(@"
UPDATE kfgz_deployments
SET state=3,battle_id=$4,updated_at=now()
WHERE round_id=$1 AND player_id=$2 AND general_id=ANY($3);
UPDATE player_generals
SET state=3,updated_at=now()
WHERE player_id=$2 AND general_id=ANY($3);",c,t))
            {deploy.Parameters.AddWithValue(roundId);deploy.Parameters.AddWithValue(playerId);deploy.Parameters.AddWithValue(ids);deploy.Parameters.AddWithValue(battleId);await deploy.ExecuteNonQueryAsync(ct);}

            await t.CommitAsync(ct);
        }

        await push.BroadcastAsync("battle.updated",new{battleId,reason="kfgz.reinforce",playerId,side,generalIds=ids},ct);
        await push.BroadcastAsync("kfgz.world",new{reason="reinforce",battleId,cityId,playerId,generalIds=ids},ct);
        return new(battleId,cityId,side,ids);
    }

    async Task AddPlayerUnitAsync(NpgsqlConnection c,NpgsqlTransaction t,long battleId,int side,int sequence,long playerId,int generalId,int cityId,CancellationToken ct)
    {
        int level,forces,leaderBonus,strengthBonus;
        await using(var q=new NpgsqlCommand("SELECT level,forces,leader_bonus,strength_bonus FROM player_generals WHERE player_id=$1 AND general_id=$2 FOR UPDATE",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);
            await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("KFGZ_REINFORCE_GENERAL_MISSING","General does not exist.",404);
            level=r.GetInt32(0);forces=r.GetInt32(1);leaderBonus=r.GetInt32(2);strengthBonus=r.GetInt32(3);
        }
        if(forces<=0)throw new GameException("KFGZ_REINFORCE_NO_FORCES","General has no forces to reinforce with.",409);
        if(!content.Generals.TryGetValue(generalId,out var general)||!content.Troops.TryGetValue(general.TroopId,out var troop))
            throw new GameException("KFGZ_REINFORCE_STATIC_MISSING","General troop data is missing.",500);

        var equip=await EquipmentAsync(c,t,playerId,generalId,ct);
        var techAtt=await technologies.GetCompletedIntEffectAsync(playerId,30,0,ct,c,t);
        var techDef=await technologies.GetCompletedIntEffectAsync(playerId,30,1,ct,c,t);
        var techHp=await technologies.GetCompletedIntEffectAsync(playerId,30,2,ct,c,t);
        var columns=2+await technologies.GetCompletedIntEffectAsync(playerId,4,0,ct,c,t);
        var techTacticAtt=await technologies.GetCompletedIntEffectAsync(playerId,10,0,ct,c,t);
        var techTacticDef=await technologies.GetCompletedIntEffectAsync(playerId,13,0,ct,c,t);
        var max=MaxHp(level,equip.hp+techHp,columns);
        var hp=Math.Min(max,forces);hp-=hp%3;
        content.Tactics.TryGetValue(general.TacticId,out var tactic);
        var terrain=content.WorldCities.TryGetValue(cityId,out var worldCity)?worldCity.Terrain:0;
        var strategy=DefaultStrategy(troop,terrain,side);

        await using var insert=new NpgsqlCommand(@"
INSERT INTO battle_units(
 battle_id,side,sequence,player_id,general_id,troop_id,name,level,attack,defense,leader,strength,hp,max_hp,is_npc,
 quality,tactic_id,tactic_damage,tactic_range,strategy_id,tech_tactic_attack,tech_tactic_defense,is_phantom)
VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,false,$15,$16,$17,$18,$19,$20,$21,false)",c,t);
        insert.Parameters.AddWithValue(battleId);insert.Parameters.AddWithValue(side);insert.Parameters.AddWithValue(sequence);insert.Parameters.AddWithValue(playerId);
        insert.Parameters.AddWithValue(general.Id);insert.Parameters.AddWithValue(general.TroopId);insert.Parameters.AddWithValue(general.Name);insert.Parameters.AddWithValue(level);
        insert.Parameters.AddWithValue(150+(level-1)*3+troop.Attack+equip.att+techAtt);insert.Parameters.AddWithValue(50+(level-1)+troop.Defense+equip.def+techDef);
        insert.Parameters.AddWithValue(general.Leader+leaderBonus);insert.Parameters.AddWithValue(general.Strength+strengthBonus);insert.Parameters.AddWithValue(hp);insert.Parameters.AddWithValue(max);
        insert.Parameters.AddWithValue(general.Quality);insert.Parameters.AddWithValue(general.TacticId);insert.Parameters.AddWithValue(tactic?.DamageExponent??0);insert.Parameters.AddWithValue(tactic?.Range??0);
        insert.Parameters.AddWithValue(strategy);insert.Parameters.AddWithValue(techTacticAtt);insert.Parameters.AddWithValue(techTacticDef);await insert.ExecuteNonQueryAsync(ct);
    }

    static async Task<(int att,int def,int hp)> EquipmentAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"SELECT
COALESCE(sum(CASE WHEN goods_type IN(1,2) THEN attribute ELSE 0 END),0),
COALESCE(sum(CASE WHEN goods_type IN(3,4) THEN attribute ELSE 0 END),0),
COALESCE(sum(CASE WHEN goods_type NOT IN(1,2,3,4,10,14) THEN attribute ELSE 0 END),0)
FROM player_equipment WHERE player_id=$1 AND owner_general_id=$2",c,t);
        q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
        return((int)r.GetInt64(0),(int)r.GetInt64(1),(int)r.GetInt64(2));
    }

    static int MaxHp(int level,int bonus,int columns){var hp=(1200+(level-1)*24+bonus)/3*Math.Max(1,columns)*3;return hp-hp%6;}
    static int DefaultStrategy(TroopDefinition troop,int terrain,int side)=>ParseStrategies(troop,terrain,side).FirstOrDefault();
    static int[] ParseStrategies(TroopDefinition troop,int terrain,int side)
    {
        var raw=side==1?troop.TerrainStrategy:troop.TerrainStrategyDefense;
        foreach(var group in raw.Split(';'))
        {
            var p=group.Split('|');
            if(p.Length<2||!int.TryParse(p[0],out var id)||id!=terrain)continue;
            return p[1].Split(',').Select(x=>int.TryParse(x,out var value)?value:0).Where(x=>x!=0).Distinct().ToArray();
        }
        return [];
    }

    static bool RoadOpen(KfgzWorldRoadDefinition r,DateTimeOffset starts)=>r.RoadType!=1||DynamicRoad(r,starts).open;
    static (bool open,int seconds) DynamicRoad(KfgzWorldRoadDefinition r,DateTimeOffset starts)
    {
        var elapsed=Math.Max(0,(int)(DateTimeOffset.UtcNow-starts).TotalSeconds);
        var first=Math.Max(1,r.Disconnect-r.Disconnect/2)*60;
        if(elapsed<first)return(false,first-elapsed);
        elapsed-=first;var open=true;
        while(true){var span=Math.Max(1,open?r.Connect:r.Disconnect)*60;if(elapsed<span)return(open,span-elapsed);elapsed-=span;open=!open;}
    }
}
