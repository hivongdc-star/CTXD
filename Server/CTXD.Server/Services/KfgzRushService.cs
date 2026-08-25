using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfgzRushRequest(int[] GeneralIds,int CityId);
public sealed record KfgzRushResult(long SourceBattleId,int TargetCityId,long? TargetBattleId,int[] GeneralIds,bool Captured);

public sealed class KfgzRushService(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technologies,
    BattleService battles,
    GamePushHub push)
{
    public async Task<KfgzRushResult> RushAsync(long playerId,long battleId,KfgzRushRequest request,CancellationToken ct)
    {
        var generalIds=(request.GeneralIds??[]).Distinct().ToArray();
        if(generalIds.Length==0)throw new GameException("KFGZ_RUSH_GENERALS_REQUIRED","Select at least one general to rush.");

        long roundId,seasonId;
        int worldId,fromCity,side,force1,force2;
        DateTimeOffset roundStarts;
        long? targetBattleId=null;
        bool captured=false;
        bool sourceSideEmpty=false;
        long defenderPlayer=0;
        int[] defenderGenerals=[];

        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            await using(var meta=new NpgsqlCommand(@"
SELECT kb.round_id,r.season_id,r.world_id,r.starts_at,kb.city_id,
       CASE WHEN kb.attacker_player_id=$2 THEN kb.attacker_side WHEN kb.defender_player_id=$2 THEN kb.defender_side ELSE 0 END,
       r.force1,r.force2
FROM kfgz_battles kb
JOIN kfgz_rounds r ON r.id=kb.round_id
JOIN world_battle_handoffs h ON h.id=kb.battle_id
JOIN battles b ON b.id=kb.battle_id
WHERE kb.battle_id=$1 AND kb.state=1 AND r.state=1 AND h.battle_type=18 AND b.status=0
FOR UPDATE OF kb,r,b",c,t))
            {
                meta.Parameters.AddWithValue(battleId);meta.Parameters.AddWithValue(playerId);
                await using var r=await meta.ExecuteReaderAsync(ct);
                if(!await r.ReadAsync(ct))throw new GameException("KFGZ_RUSH_BATTLE_INVALID","Rush requires an active KFGZ battle.",409);
                roundId=r.GetInt64(0);seasonId=r.GetInt64(1);worldId=r.GetInt32(2);roundStarts=r.GetFieldValue<DateTimeOffset>(3);fromCity=r.GetInt32(4);side=r.GetInt32(5);force1=r.GetInt16(6);force2=r.GetInt16(7);
            }
            if(side is not(1 or 2))throw new GameException("KFGZ_RUSH_NOT_PARTICIPANT","Player is not a tracked participant of this KFGZ battle.",403);

            var selected=new List<(long id,int general,int hp,int maxHp,int side,int sequence)>();
            await using(var q=new NpgsqlCommand(@"
SELECT id,general_id,hp,max_hp,side,sequence
FROM battle_units
WHERE battle_id=$1 AND player_id=$2 AND general_id=ANY($3) AND hp>0 AND detached=false AND is_phantom=false
FOR UPDATE",c,t))
            {
                q.Parameters.AddWithValue(battleId);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalIds);
                await using var r=await q.ExecuteReaderAsync(ct);
                while(await r.ReadAsync(ct))selected.Add((r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt16(4),r.GetInt32(5)));
            }
            if(selected.Count!=generalIds.Length||selected.Any(x=>x.side!=side))throw new GameException("KFGZ_RUSH_GENERAL_INVALID","Every selected general must be an active real unit on the player's side.",409);

            int friendly,enemy;
            await using(var counts=new NpgsqlCommand(@"
SELECT count(*) FILTER(WHERE side=$2 AND hp>0 AND detached=false)::int,
       count(*) FILTER(WHERE side<>$2 AND hp>0 AND detached=false)::int
FROM battle_units WHERE battle_id=$1",c,t))
            {
                counts.Parameters.AddWithValue(battleId);counts.Parameters.AddWithValue(side);
                await using var r=await counts.ExecuteReaderAsync(ct);await r.ReadAsync(ct);friendly=r.GetInt32(0);enemy=r.GetInt32(1);
            }
            var tech=await technologies.GetCompletedIntEffectAsync(playerId,39,0,ct,c,t);
            var ratio=3-(tech>0?tech:0);
            if(friendly<=ratio*enemy)throw new GameException("KFGZ_RUSH_FORCE_RATIO_LOW","Legacy rush requires friendly camp size to exceed (3-TechEffect39) times the enemy camp.",409);

            long frontId;
            await using(var front=new NpgsqlCommand("SELECT id FROM battle_units WHERE battle_id=$1 AND side=$2 AND hp>0 AND detached=false ORDER BY sequence LIMIT 1",c,t))
            {front.Parameters.AddWithValue(battleId);front.Parameters.AddWithValue(side);frontId=Convert.ToInt64(await front.ExecuteScalarAsync(ct)??0L);}
            if(selected.Any(x=>x.id==frontId))throw new GameException("KFGZ_RUSH_GENERAL_ON_QUEUE","The front combat general cannot rush while queued for combat.",409);
            if(selected.Any(x=>x.maxHp<=0||x.hp*1d/x.maxHp<0.05d))throw new GameException("KFGZ_RUSH_HP_LOW","Every rushing general must have at least 5% forces remaining.",409);

            if(!content.KfgzWorldCities.TryGetValue(request.CityId,out var targetCity)||targetCity.World!=worldId)
                throw new GameException("KFGZ_RUSH_CITY_INVALID","Rush target is not in this KFGZ world.",404);
            if(targetCity.CityType==1&&targetCity.InitialForce!=side)
                throw new GameException("KFGZ_ENEMY_CAPITAL_FORBIDDEN","Legacy KFGZ rush cannot enter the opposing capital.",409);
            var road=content.KfgzWorldRoads.Values.FirstOrDefault(v=>v.World==worldId&&(v.From==fromCity&&v.To==request.CityId||v.From==request.CityId&&v.To==fromCity));
            if(road is null||!RoadOpen(road,roundStarts))throw new GameException("KFGZ_RUSH_ROAD_CLOSED","Rush target must be connected by an open legacy KFGZ road.",409);

            await using(var activeTarget=new NpgsqlCommand("SELECT battle_id FROM kfgz_battles WHERE round_id=$1 AND city_id=$2 AND state=1 FOR UPDATE",c,t))
            {
                activeTarget.Parameters.AddWithValue(roundId);activeTarget.Parameters.AddWithValue(request.CityId);
                var value=await activeTarget.ExecuteScalarAsync(ct);
                if(value is not null)throw new GameException("KFGZ_RUSH_TARGET_BATTLE_PENDING","Rush into an already active KFGZ battle needs the multi-player battle participant foundation; current two-player settlement model cannot represent it safely.",409);
            }

            int ownerSide;
            await using(var owner=new NpgsqlCommand("SELECT owner_side FROM kfgz_city_states WHERE round_id=$1 AND city_id=$2 FOR UPDATE",c,t))
            {owner.Parameters.AddWithValue(roundId);owner.Parameters.AddWithValue(request.CityId);ownerSide=Convert.ToInt16(await owner.ExecuteScalarAsync(ct)??0);}
            if(ownerSide==side)throw new GameException("KFGZ_RUSH_OWN_CITY","Legacy rush cannot enter an unopposed city already owned by the player's side.",409);

            var enemyForce=side==1?force2:force1;
            await using(var defenders=new NpgsqlCommand(@"
SELECT player_id,array_agg(general_id ORDER BY general_id)
FROM kfgz_deployments
WHERE round_id=$1 AND city_id=$2 AND state=1 AND player_id<>$3
  AND player_id IN(SELECT player_id FROM kfgz_signups WHERE season_id=$4 AND force_id=$5)
GROUP BY player_id ORDER BY player_id LIMIT 1 FOR UPDATE",c,t))
            {
                defenders.Parameters.AddWithValue(roundId);defenders.Parameters.AddWithValue(request.CityId);defenders.Parameters.AddWithValue(playerId);defenders.Parameters.AddWithValue(seasonId);defenders.Parameters.AddWithValue(enemyForce);
                await using var r=await defenders.ExecuteReaderAsync(ct);
                if(await r.ReadAsync(ct)){defenderPlayer=r.GetInt64(0);defenderGenerals=r.GetFieldValue<int[]>(1);}
            }

            await using(var detach=new NpgsqlCommand("UPDATE battle_units SET detached=true WHERE id=ANY($1)",c,t))
            {detach.Parameters.AddWithValue(selected.Select(x=>x.id).ToArray());await detach.ExecuteNonQueryAsync(ct);}

            if(defenderPlayer==0)
            {
                await using(var move=new NpgsqlCommand(@"
UPDATE kfgz_deployments SET city_id=$4,state=1,battle_id=NULL,updated_at=now()
WHERE round_id=$1 AND player_id=$2 AND general_id=ANY($3);
UPDATE player_generals SET state=1,updated_at=now() WHERE player_id=$2 AND general_id=ANY($3);",c,t))
                {move.Parameters.AddWithValue(roundId);move.Parameters.AddWithValue(playerId);move.Parameters.AddWithValue(generalIds);move.Parameters.AddWithValue(request.CityId);await move.ExecuteNonQueryAsync(ct);}
                if(ownerSide!=side)
                {
                    await using var occupy=new NpgsqlCommand(@"
UPDATE kfgz_city_states SET owner_side=$3,updated_at=now() WHERE round_id=$1 AND city_id=$2 AND owner_side<>$3;
UPDATE kfgz_player_stats SET occupy_city=occupy_city+1,updated_at=now() WHERE season_id=$4 AND player_id=$5;",c,t);
                    occupy.Parameters.AddWithValue(roundId);occupy.Parameters.AddWithValue(request.CityId);occupy.Parameters.AddWithValue(side);occupy.Parameters.AddWithValue(seasonId);occupy.Parameters.AddWithValue(playerId);await occupy.ExecuteNonQueryAsync(ct);captured=true;
                }
            }
            else
            {
                var attackerForce=side==1?force1:force2;
                var lead=generalIds[0];
                await using(var handoff=new NpgsqlCommand(@"
INSERT INTO world_battle_handoffs(city_id,attacker_player_id,attacker_general_id,attacker_force_id,defender_force_id,battle_type,result_payload)
VALUES($1,$2,$3,$4,$5,18,jsonb_build_object('defenderPlayerId',$6,'attackerGenerals',to_jsonb($7::integer[]),'defenderGenerals',to_jsonb($8::integer[]),'kfgzRoundId',$9,'kfgzSide',$10,'rush',true))
RETURNING id",c,t))
                {
                    handoff.Parameters.AddWithValue(request.CityId);handoff.Parameters.AddWithValue(playerId);handoff.Parameters.AddWithValue(lead);handoff.Parameters.AddWithValue(attackerForce);handoff.Parameters.AddWithValue(enemyForce);handoff.Parameters.AddWithValue(defenderPlayer);handoff.Parameters.AddWithValue(generalIds);handoff.Parameters.AddWithValue(defenderGenerals);handoff.Parameters.AddWithValue(roundId);handoff.Parameters.AddWithValue(side);
                    targetBattleId=Convert.ToInt64(await handoff.ExecuteScalarAsync(ct));
                }
                long matchId;
                await using(var match=new NpgsqlCommand("INSERT INTO kfgz_battles(round_id,city_id,attacker_player_id,defender_player_id,attacker_side,defender_side,battle_id) VALUES($1,$2,$3,$4,$5,$6,$7) RETURNING id",c,t))
                {match.Parameters.AddWithValue(roundId);match.Parameters.AddWithValue(request.CityId);match.Parameters.AddWithValue(playerId);match.Parameters.AddWithValue(defenderPlayer);match.Parameters.AddWithValue(side);match.Parameters.AddWithValue(side==1?2:1);match.Parameters.AddWithValue(targetBattleId.Value);matchId=Convert.ToInt64(await match.ExecuteScalarAsync(ct));}
                await using(var deploy=new NpgsqlCommand(@"
UPDATE kfgz_deployments SET city_id=$4,state=3,battle_id=$5,updated_at=now() WHERE round_id=$1 AND player_id=$2 AND general_id=ANY($3);
UPDATE kfgz_deployments SET state=3,battle_id=$5,updated_at=now() WHERE round_id=$1 AND player_id=$6 AND general_id=ANY($7);
UPDATE player_generals SET state=3,updated_at=now() WHERE (player_id=$2 AND general_id=ANY($3)) OR (player_id=$6 AND general_id=ANY($7));",c,t))
                {deploy.Parameters.AddWithValue(roundId);deploy.Parameters.AddWithValue(playerId);deploy.Parameters.AddWithValue(generalIds);deploy.Parameters.AddWithValue(request.CityId);deploy.Parameters.AddWithValue(targetBattleId.Value);deploy.Parameters.AddWithValue(defenderPlayer);deploy.Parameters.AddWithValue(defenderGenerals);await deploy.ExecuteNonQueryAsync(ct);}
                await push.SendAsync(defenderPlayer,"kfgz.battle",new{matchId,battleId=targetBattleId.Value,cityId=request.CityId,opponent=playerId,rush=true},ct);
            }

            await using(var empty=new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM battle_units WHERE battle_id=$1 AND side=$2 AND hp>0 AND detached=false)",c,t))
            {empty.Parameters.AddWithValue(battleId);empty.Parameters.AddWithValue(side);sourceSideEmpty=Convert.ToBoolean(await empty.ExecuteScalarAsync(ct));}
            await t.CommitAsync(ct);
        }

        if(sourceSideEmpty)await battles.AdvanceAsync(playerId,battleId,ct);
        await push.BroadcastAsync("kfgz.world",new{reason="rush",sourceBattleId=battleId,targetCityId=request.CityId,targetBattleId,generalIds},ct);
        return new(battleId,request.CityId,targetBattleId,generalIds,captured);
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
