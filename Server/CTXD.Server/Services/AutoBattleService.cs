using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record AutoBattleStartRequest(int CityId);
public sealed record AutoBattleView(bool TechUnlocked,int State,int TargetCityId,int AutoType,long Cd,long Exp,long Lost,int Result);

public sealed class AutoBattleService(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technologies,
    ResourceProductionService production,
    WorldService world,
    BattleService battles)
{
    const int TechKey=59;
    const long FoodCost=50_000;
    static readonly TimeSpan DurationLimit=TimeSpan.FromMinutes(30);
    static readonly TimeSpan CheckInterval=TimeSpan.FromSeconds(10);
    readonly WorldBattleReinforcementService reinforcement=new(db,content,technologies,battles);

    sealed record Row(
        long PlayerId,int ForceId,int TargetCityId,int State,int AutoType,long Exp,long Lost,int Result,
        long BaselineExp,long BaselineLost,DateTimeOffset? StartedAt,DateTimeOffset? EndsAt,DateTimeOffset? NeedCheckAt);
    sealed record ActiveBattle(long Id,int AttackerForce,int DefenderForce,long LeadPlayer,int SourceCity)
    {
        public bool HasForce(int force)=>AttackerForce==force||DefenderForce==force;
    }

    public async Task<AutoBattleView> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var unlocked=await technologies.GetCompletedIntEffectAsync(playerId,TechKey,0,ct,c,t)>0;
        var row=await ReadRowAsync(c,t,playerId,true,ct);
        if(row is not null)row=await RefreshStatsAsync(c,t,row,ct);
        await t.CommitAsync(ct);
        return View(unlocked,row);
    }

    public async Task<AutoBattleView> StartAsync(long playerId,int cityId,CancellationToken ct)
    {
        if(!content.WorldCities.ContainsKey(cityId))throw new GameException("AUTO_BATTLE_CITY_NOT_FOUND","Auto Battle target city does not exist.",404);

        // Ensure canonical world runtime/player visibility rows exist before the start transaction.
        await world.GetAsync(playerId,ct);
        var now=DateTimeOffset.UtcNow;
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            var existing=await ReadRowAsync(c,t,playerId,true,ct);
            if(existing?.State==1)throw new GameException("AUTO_BATTLE_ALREADY_ACTIVE","Auto Battle is already active.",409);

            int force;
            await using(var player=new NpgsqlCommand("SELECT force_id FROM players WHERE id=$1 FOR UPDATE",c,t))
            {
                player.Parameters.AddWithValue(playerId);
                var value=await player.ExecuteScalarAsync(ct);
                if(value is null)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
                force=Convert.ToInt32(value);
            }
            // The player row serializes concurrent start requests. Re-read after acquiring it so a
            // request that waited for another start cannot charge the 50k food a second time.
            existing=await ReadRowAsync(c,t,playerId,true,ct);
            if(existing?.State==1)throw new GameException("AUTO_BATTLE_ALREADY_ACTIVE","Auto Battle is already active.",409);

            if(await technologies.GetCompletedIntEffectAsync(playerId,TechKey,0,ct,c,t)<=0)
                throw new GameException("AUTO_BATTLE_TECH_REQUIRED","Legacy Auto Battle requires completed TechEffect 59.",403);

            int owner;
            await using(var city=new NpgsqlCommand("SELECT owner_force_id FROM world_cities WHERE city_id=$1 FOR UPDATE",c,t))
            {
                city.Parameters.AddWithValue(cityId);
                var value=await city.ExecuteScalarAsync(ct);
                if(value is null)throw new GameException("AUTO_BATTLE_CITY_NOT_FOUND","Auto Battle target city does not exist.",404);
                owner=Convert.ToInt32(value);
            }
            var activeBattle=await HasActiveCityBattleAsync(c,t,cityId,ct);
            if(owner==force&&!activeBattle)
                throw new GameException("AUTO_BATTLE_DEFENSE_INACTIVE","Legacy defensive Auto Battle can start only while the friendly target city is under attack.",409);

            await using(var generals=new NpgsqlCommand(@"
SELECT 1 FROM player_generals
WHERE player_id=$1 AND general_type=2 AND state NOT IN(24,25,26,27,28)
LIMIT 1",c,t))
            {
                generals.Parameters.AddWithValue(playerId);
                if(await generals.ExecuteScalarAsync(ct)is null)
                    throw new GameException("AUTO_BATTLE_NO_AVAILABLE_GENERAL","No military general can enter legacy Auto Battle.",409);
            }

            await production.AccrueAndGetAsync(playerId,ct,c,t);
            await using(var pay=new NpgsqlCommand("UPDATE player_resources SET food=food-$2 WHERE player_id=$1 AND food>=$2",c,t))
            {
                pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(FoodCost);
                if(await pay.ExecuteNonQueryAsync(ct)==0)throw new GameException("AUTO_BATTLE_FOOD_NOT_ENOUGH","Auto Battle requires 50,000 food.",409);
            }

            var baselineExp=await BattleExpTotalAsync(c,t,playerId,ct);
            var baselineLost=await ForcesTotalAsync(c,t,playerId,ct);
            var autoType=owner==force?2:1;
            await using var upsert=new NpgsqlCommand(@"
INSERT INTO player_auto_battles(
 player_id,force_id,target_city_id,state,auto_type,exp,lost,result,baseline_exp,baseline_lost,started_at,ends_at,need_check_at,updated_at)
VALUES($1,$2,$3,1,$4,0,0,0,$5,$6,$7,$8,$7,now())
ON CONFLICT(player_id) DO UPDATE SET
 force_id=excluded.force_id,target_city_id=excluded.target_city_id,state=1,auto_type=excluded.auto_type,
 exp=0,lost=0,result=0,baseline_exp=excluded.baseline_exp,baseline_lost=excluded.baseline_lost,
 started_at=excluded.started_at,ends_at=excluded.ends_at,need_check_at=excluded.need_check_at,updated_at=now()",c,t);
            upsert.Parameters.AddWithValue(playerId);upsert.Parameters.AddWithValue(force);upsert.Parameters.AddWithValue(cityId);upsert.Parameters.AddWithValue(autoType);
            upsert.Parameters.AddWithValue(baselineExp);upsert.Parameters.AddWithValue(baselineLost);upsert.Parameters.AddWithValue(now);upsert.Parameters.AddWithValue(now.Add(DurationLimit));
            await upsert.ExecuteNonQueryAsync(ct);
            await t.CommitAsync(ct);
        }
        return await GetAsync(playerId,ct);
    }

    public async Task<AutoBattleView> StopAsync(long playerId,CancellationToken ct)
    {
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            var row=await ReadRowAsync(c,t,playerId,true,ct);
            if(row is null||row.State!=1)throw new GameException("AUTO_BATTLE_NOT_ACTIVE","Auto Battle is not active.",409);
            row=await RefreshStatsAsync(c,t,row,ct);
            await using(var stop=new NpgsqlCommand(@"
UPDATE player_auto_battles
SET state=0,target_city_id=0,auto_type=0,need_check_at=NULL,updated_at=now()
WHERE player_id=$1",c,t))
            {stop.Parameters.AddWithValue(playerId);await stop.ExecuteNonQueryAsync(ct);}
            await StopMovementsAsync(c,t,playerId,ct);
            await t.CommitAsync(ct);
        }
        return await GetAsync(playerId,ct);
    }

    public async Task<long[]> FindDuePlayersAsync(CancellationToken ct)
    {
        var result=new List<long>();
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"
SELECT player_id FROM player_auto_battles
WHERE state=1 AND COALESCE(need_check_at,now())<=now()
ORDER BY need_check_at NULLS FIRST,player_id
LIMIT 200",c);
        await using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))result.Add(r.GetInt64(0));
        return result.ToArray();
    }

    public async Task<AutoBattleView?> TickAsync(long playerId,CancellationToken ct)
    {
        Row row;
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            var current=await ReadRowAsync(c,t,playerId,true,ct);
            if(current is null||current.State!=1){await t.CommitAsync(ct);return null;}
            row=await RefreshStatsAsync(c,t,current,ct);

            var resolvedWinner=await LatestResolvedWinnerAsync(c,t,row,ct);
            int? finishResult=null;
            if(row.AutoType==1&&resolvedWinner==row.ForceId)finishResult=1;
            else if(row.AutoType==2&&resolvedWinner.HasValue)finishResult=resolvedWinner.Value==row.ForceId?3:4;
            else if(row.EndsAt.HasValue&&row.EndsAt.Value<=DateTimeOffset.UtcNow)finishResult=row.AutoType==1?2:5;

            if(finishResult.HasValue)
            {
                await FinishAsync(c,t,row,finishResult.Value,ct);
                await t.CommitAsync(ct);
                return await GetAsync(playerId,ct);
            }

            await using(var due=new NpgsqlCommand("UPDATE player_auto_battles SET need_check_at=$2,updated_at=now() WHERE player_id=$1 AND state=1",c,t))
            {due.Parameters.AddWithValue(playerId);due.Parameters.AddWithValue(DateTimeOffset.UtcNow.Add(CheckInterval));await due.ExecuteNonQueryAsync(ct);}
            await t.CommitAsync(ct);
        }

        await PrepareDueMovementAsync(playerId,ct);
        var active=await ActiveBattleAsync(row.TargetCityId,ct);
        var participant=active is not null&&active.HasForce(row.ForceId);
        if(participant&&active is not null)
        {
            try{await battles.GetAsync(active.LeadPlayer,active.Id,ct);}
            catch(GameException ex)when(IsTransient(ex.Code)){}
        }

        var generals=await MilitaryGeneralsAsync(playerId,ct);
        foreach(var g in generals)
        {
            if(g.Forces<=0||g.State is 6 or 24 or 25 or 26 or 27 or 28||g.State>1)continue;
            try
            {
                if(active is not null)
                {
                    if(!participant)
                    {
                        if(g.LocationId==row.TargetCityId||Road(g.LocationId,row.TargetCityId)is not null)continue;
                        await world.AutoMoveAsync(playerId,g.GeneralId,row.TargetCityId,ct);
                        continue;
                    }
                    if(g.LocationId==row.TargetCityId)
                    {
                        await reinforcement.JoinAsync(playerId,active.Id,g.GeneralId,ct);
                        continue;
                    }
                    if(row.ForceId==active.AttackerForce&&g.LocationId==active.SourceCity)
                    {
                        await battles.JoinAttackerAsync(playerId,active.Id,g.GeneralId,ct);
                        continue;
                    }
                    if(Road(g.LocationId,row.TargetCityId)is not null)
                    {
                        await StartFinalLegAsync(playerId,g.GeneralId,row.ForceId,g.LocationId,row.TargetCityId,ct);
                        continue;
                    }
                    await world.AutoMoveAsync(playerId,g.GeneralId,row.TargetCityId,ct);
                    continue;
                }

                if(row.AutoType!=1)continue;
                if(g.LocationId==row.TargetCityId)
                {
                    if(await StartBattleAtTargetAsync(playerId,g.GeneralId,row.ForceId,row.TargetCityId,ct))break;
                    continue;
                }
                await world.AutoMoveAsync(playerId,g.GeneralId,row.TargetCityId,ct);
            }
            catch(GameException ex)when(IsTransient(ex.Code)){}
        }

        foreach(var battleId in await ActivePlayerWorldBattlesAsync(playerId,ct))
        {
            try{await battles.AdvanceAsync(playerId,battleId,ct);}
            catch(GameException ex)when(IsTransient(ex.Code)){}
        }
        return await GetAsync(playerId,ct);
    }

    // Called by WorldMovementWorker before WorldService settles a due move. If the final hostile
    // leg now points into an already-running target battle, legacy assembleMove keeps marching
    // into that battle instead of trying to create a duplicate city battle.
    public async Task PrepareDueMovementAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var row=await ReadRowAsync(c,t,playerId,true,ct);
        if(row is null||row.State!=1){await t.CommitAsync(ct);return;}

        long? battleId=null;int attacker=0,defender=0;
        await using(var active=new NpgsqlCommand(@"
SELECT id,attacker_force_id,defender_force_id
FROM world_battle_handoffs
WHERE city_id=$1 AND status=0 AND battle_type IN(3,14)
ORDER BY id LIMIT 1",c,t))
        {
            active.Parameters.AddWithValue(row.TargetCityId);
            await using var r=await active.ExecuteReaderAsync(ct);
            if(await r.ReadAsync(ct)){battleId=r.GetInt64(0);attacker=r.GetInt16(1);defender=r.GetInt16(2);}
        }
        if(!battleId.HasValue){await t.CommitAsync(ct);return;}
        var participant=attacker==row.ForceId||defender==row.ForceId;

        var moves=new List<(int GeneralId,int CurrentCity,int[] Path,int PathIndex)>();
        await using(var due=new NpgsqlCommand(@"
SELECT general_id,to_city_id,path_city_ids,path_index
FROM player_world_moves
WHERE player_id=$1 AND arrives_at<=now()
FOR UPDATE",c,t))
        {
            due.Parameters.AddWithValue(playerId);
            await using var r=await due.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))moves.Add((r.GetInt32(0),r.GetInt32(1),r.GetFieldValue<int[]>(2),r.GetInt32(3)));
        }

        foreach(var move in moves)
        {
            var next=move.PathIndex+1;
            if(next>=move.Path.Length||move.Path[next]!=row.TargetCityId)continue;
            await using(var location=new NpgsqlCommand("UPDATE player_generals SET location_id=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t))
            {location.Parameters.AddWithValue(playerId);location.Parameters.AddWithValue(move.GeneralId);location.Parameters.AddWithValue(move.CurrentCity);await location.ExecuteNonQueryAsync(ct);}

            if(!participant)
            {
                await using(var delete=new NpgsqlCommand("DELETE FROM player_world_moves WHERE player_id=$1 AND general_id=$2",c,t))
                {delete.Parameters.AddWithValue(playerId);delete.Parameters.AddWithValue(move.GeneralId);await delete.ExecuteNonQueryAsync(ct);}
                await using(var idle=new NpgsqlCommand("UPDATE player_generals SET state=1,updated_at=now() WHERE player_id=$1 AND general_id=$2 AND state=6",c,t))
                {idle.Parameters.AddWithValue(playerId);idle.Parameters.AddWithValue(move.GeneralId);await idle.ExecuteNonQueryAsync(ct);}
                continue;
            }

            var road=Road(move.CurrentCity,row.TargetCityId);
            if(road is null)continue;
            var speed=GeneralSpeed(move.GeneralId);
            var now=DateTimeOffset.UtcNow;
            await using var update=new NpgsqlCommand(@"
UPDATE player_world_moves
SET road_id=$3,from_city_id=$4,to_city_id=$5,started_at=$6,arrives_at=$7,path_index=$8
WHERE player_id=$1 AND general_id=$2",c,t);
            update.Parameters.AddWithValue(playerId);update.Parameters.AddWithValue(move.GeneralId);update.Parameters.AddWithValue(road.Id);
            update.Parameters.AddWithValue(move.CurrentCity);update.Parameters.AddWithValue(row.TargetCityId);update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(now.Add(MoveDuration(road.Length,speed)));update.Parameters.AddWithValue(next);
            await update.ExecuteNonQueryAsync(ct);
        }
        await t.CommitAsync(ct);
    }

    async Task<bool> StartFinalLegAsync(long playerId,int generalId,int force,int fromCity,int targetCity,CancellationToken ct)
    {
        var road=Road(fromCity,targetCity);if(road is null)return false;
        var speed=GeneralSpeed(generalId);var now=DateTimeOffset.UtcNow;
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await using(var active=new NpgsqlCommand(@"
SELECT 1 FROM world_battle_handoffs
WHERE city_id=$1 AND status=0 AND battle_type IN(3,14) AND (attacker_force_id=$2 OR defender_force_id=$2)
LIMIT 1",c,t))
        {active.Parameters.AddWithValue(targetCity);active.Parameters.AddWithValue(force);if(await active.ExecuteScalarAsync(ct)is null){await t.CommitAsync(ct);return false;}}
        await using(var general=new NpgsqlCommand(@"
SELECT 1 FROM player_generals
WHERE player_id=$1 AND general_id=$2 AND general_type=2 AND state<=1 AND forces>0 AND location_id=$3
FOR UPDATE",c,t))
        {general.Parameters.AddWithValue(playerId);general.Parameters.AddWithValue(generalId);general.Parameters.AddWithValue(fromCity);if(await general.ExecuteScalarAsync(ct)is null){await t.CommitAsync(ct);return false;}}
        await using(var exists=new NpgsqlCommand("SELECT 1 FROM player_world_moves WHERE player_id=$1 AND general_id=$2 FOR UPDATE",c,t))
        {exists.Parameters.AddWithValue(playerId);exists.Parameters.AddWithValue(generalId);if(await exists.ExecuteScalarAsync(ct)is not null){await t.CommitAsync(ct);return false;}}

        await using(var insert=new NpgsqlCommand(@"
INSERT INTO player_world_moves(player_id,general_id,road_id,from_city_id,to_city_id,started_at,arrives_at,path_city_ids,path_index)
VALUES($1,$2,$3,$4,$5,$6,$7,$8,1)",c,t))
        {
            insert.Parameters.AddWithValue(playerId);insert.Parameters.AddWithValue(generalId);insert.Parameters.AddWithValue(road.Id);insert.Parameters.AddWithValue(fromCity);
            insert.Parameters.AddWithValue(targetCity);insert.Parameters.AddWithValue(now);insert.Parameters.AddWithValue(now.Add(MoveDuration(road.Length,speed)));insert.Parameters.AddWithValue(new[]{fromCity,targetCity});
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using(var state=new NpgsqlCommand("UPDATE player_generals SET state=6,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t))
        {state.Parameters.AddWithValue(playerId);state.Parameters.AddWithValue(generalId);await state.ExecuteNonQueryAsync(ct);}
        await using(var quest=new NpgsqlCommand("UPDATE player_quest_runtime SET world_moves=world_moves+1,updated_at=now() WHERE player_id=$1",c,t))
        {quest.Parameters.AddWithValue(playerId);await quest.ExecuteNonQueryAsync(ct);}
        await using(var focus=new NpgsqlCommand("UPDATE player_world SET focus_general_id=$2,updated_at=now() WHERE player_id=$1",c,t))
        {focus.Parameters.AddWithValue(playerId);focus.Parameters.AddWithValue(generalId);await focus.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);return true;
    }

    async Task<bool> StartBattleAtTargetAsync(long playerId,int generalId,int force,int targetCity,CancellationToken ct)
    {
        long battleId;
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            int owner;
            await using(var city=new NpgsqlCommand("SELECT owner_force_id FROM world_cities WHERE city_id=$1 FOR UPDATE",c,t))
            {city.Parameters.AddWithValue(targetCity);owner=Convert.ToInt32(await city.ExecuteScalarAsync(ct)??0);}
            if(owner==force){await t.CommitAsync(ct);return false;}
            await using(var check=new NpgsqlCommand(@"
SELECT 1 FROM player_generals
WHERE player_id=$1 AND general_id=$2 AND general_type=2 AND state<=1 AND forces>0 AND location_id=$3
FOR UPDATE",c,t))
            {check.Parameters.AddWithValue(playerId);check.Parameters.AddWithValue(generalId);check.Parameters.AddWithValue(targetCity);if(await check.ExecuteScalarAsync(ct)is null){await t.CommitAsync(ct);return false;}}
            await using var add=new NpgsqlCommand(@"
INSERT INTO world_battle_handoffs(city_id,attacker_player_id,attacker_general_id,attacker_force_id,defender_force_id,battle_type)
VALUES($1,$2,$3,$4,$5,$6)
ON CONFLICT DO NOTHING
RETURNING id",c,t);
            add.Parameters.AddWithValue(targetCity);add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(generalId);add.Parameters.AddWithValue(force);add.Parameters.AddWithValue(owner);add.Parameters.AddWithValue(targetCity is 250 or 251 or 252?14:3);
            var value=await add.ExecuteScalarAsync(ct);if(value is null){await t.CommitAsync(ct);return false;}battleId=Convert.ToInt64(value);
            await using(var state=new NpgsqlCommand("UPDATE player_generals SET state=3,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t))
            {state.Parameters.AddWithValue(playerId);state.Parameters.AddWithValue(generalId);await state.ExecuteNonQueryAsync(ct);}
            await using(var cityState=new NpgsqlCommand("UPDATE world_cities SET state=1,updated_at=now() WHERE city_id=$1",c,t))
            {cityState.Parameters.AddWithValue(targetCity);await cityState.ExecuteNonQueryAsync(ct);}
            await t.CommitAsync(ct);
        }
        await battles.GetAsync(playerId,battleId,ct);return true;
    }

    async Task<ActiveBattle?> ActiveBattleAsync(int cityId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"
SELECT h.id,h.attacker_force_id,h.defender_force_id,h.attacker_player_id,lead.location_id
FROM world_battle_handoffs h
JOIN player_generals lead ON lead.player_id=h.attacker_player_id AND lead.general_id=h.attacker_general_id
WHERE h.city_id=$1 AND h.status=0 AND h.battle_type IN(3,14)
ORDER BY h.id LIMIT 1",c);
        q.Parameters.AddWithValue(cityId);await using var r=await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetInt64(0),r.GetInt16(1),r.GetInt16(2),r.GetInt64(3),r.GetInt32(4)):null;
    }

    async Task<(int GeneralId,int State,int Forces,int LocationId)[]> MilitaryGeneralsAsync(long playerId,CancellationToken ct)
    {
        var result=new List<(int,int,int,int)>();await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("SELECT general_id,state,forces,location_id FROM player_generals WHERE player_id=$1 AND general_type=2 ORDER BY general_id",c);q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add((r.GetInt32(0),r.GetInt16(1),r.GetInt32(2),r.GetInt32(3)));return result.ToArray();
    }

    async Task<long[]> ActivePlayerWorldBattlesAsync(long playerId,CancellationToken ct)
    {
        var result=new List<long>();await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"
SELECT DISTINCT b.id
FROM battles b
JOIN world_battle_handoffs h ON h.id=b.id AND h.battle_type IN(3,14)
JOIN battle_units u ON u.battle_id=b.id
WHERE b.status=0 AND u.player_id=$1 AND u.hp>0 AND u.detached=false
ORDER BY b.id",c);q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(r.GetInt64(0));return result.ToArray();
    }

    static async Task<Row?> ReadRowAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,bool locked,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"SELECT player_id,force_id,target_city_id,state,auto_type,exp,lost,result,baseline_exp,baseline_lost,started_at,ends_at,need_check_at
FROM player_auto_battles WHERE player_id=$1"+(locked?" FOR UPDATE":""),c,t);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return null;
        return new(r.GetInt64(0),r.GetInt16(1),r.GetInt32(2),r.GetInt16(3),r.GetInt16(4),r.GetInt64(5),r.GetInt64(6),r.GetInt16(7),r.GetInt64(8),r.GetInt64(9),r.IsDBNull(10)?null:r.GetFieldValue<DateTimeOffset>(10),r.IsDBNull(11)?null:r.GetFieldValue<DateTimeOffset>(11),r.IsDBNull(12)?null:r.GetFieldValue<DateTimeOffset>(12));
    }

    static async Task<long> BattleExpTotalAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT COALESCE(sum(player_exp),0) FROM battle_rewards WHERE player_id=$1",c,t);q.Parameters.AddWithValue(playerId);return Convert.ToInt64(await q.ExecuteScalarAsync(ct)??0L);}
    static async Task<long> ForcesTotalAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT COALESCE(sum(forces),0) FROM player_generals WHERE player_id=$1 AND general_type=2",c,t);q.Parameters.AddWithValue(playerId);return Convert.ToInt64(await q.ExecuteScalarAsync(ct)??0L);}

    static async Task<Row> RefreshStatsAsync(NpgsqlConnection c,NpgsqlTransaction t,Row row,CancellationToken ct)
    {
        var currentExp=await BattleExpTotalAsync(c,t,row.PlayerId,ct);var currentForces=await ForcesTotalAsync(c,t,row.PlayerId,ct);
        var exp=Math.Max(row.Exp,Math.Max(0,currentExp-row.BaselineExp));var lost=Math.Max(row.Lost,Math.Max(0,row.BaselineLost-currentForces));
        if(exp==row.Exp&&lost==row.Lost)return row;
        await using var q=new NpgsqlCommand("UPDATE player_auto_battles SET exp=$2,lost=$3,updated_at=now() WHERE player_id=$1",c,t);q.Parameters.AddWithValue(row.PlayerId);q.Parameters.AddWithValue(exp);q.Parameters.AddWithValue(lost);await q.ExecuteNonQueryAsync(ct);
        return row with{Exp=exp,Lost=lost};
    }

    static async Task<int?> LatestResolvedWinnerAsync(NpgsqlConnection c,NpgsqlTransaction t,Row row,CancellationToken ct)
    {
        if(!row.StartedAt.HasValue)return null;
        await using var q=new NpgsqlCommand(@"
SELECT winner_force_id
FROM world_battle_handoffs
WHERE city_id=$1 AND battle_type IN(3,14) AND status<>0 AND resolved_at>=$2
  AND (attacker_force_id=$3 OR defender_force_id=$3)
ORDER BY resolved_at DESC,id DESC LIMIT 1",c,t);
        q.Parameters.AddWithValue(row.TargetCityId);q.Parameters.AddWithValue(row.StartedAt.Value);q.Parameters.AddWithValue(row.ForceId);
        var value=await q.ExecuteScalarAsync(ct);return value is null or DBNull?null:Convert.ToInt32(value);
    }

    static async Task FinishAsync(NpgsqlConnection c,NpgsqlTransaction t,Row row,int result,CancellationToken ct)
    {
        row=await RefreshStatsAsync(c,t,row,ct);
        await using var q=new NpgsqlCommand(@"
UPDATE player_auto_battles
SET state=0,target_city_id=0,auto_type=0,result=$2,exp=$3,lost=$4,need_check_at=NULL,updated_at=now()
WHERE player_id=$1 AND state=1",c,t);
        q.Parameters.AddWithValue(row.PlayerId);q.Parameters.AddWithValue(result);q.Parameters.AddWithValue(row.Exp);q.Parameters.AddWithValue(row.Lost);await q.ExecuteNonQueryAsync(ct);
    }

    static async Task StopMovementsAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        var ids=new List<int>();await using(var q=new NpgsqlCommand("DELETE FROM player_world_moves WHERE player_id=$1 RETURNING general_id",c,t)){q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add(r.GetInt32(0));}
        if(ids.Count==0)return;
        await using var state=new NpgsqlCommand("UPDATE player_generals SET state=1,updated_at=now() WHERE player_id=$1 AND general_id=ANY($2) AND state=6",c,t);state.Parameters.AddWithValue(playerId);state.Parameters.AddWithValue(ids.ToArray());await state.ExecuteNonQueryAsync(ct);
    }

    static async Task<bool> HasActiveCityBattleAsync(NpgsqlConnection c,NpgsqlTransaction t,int cityId,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT 1 FROM world_battle_handoffs WHERE city_id=$1 AND status=0 AND battle_type IN(3,14) LIMIT 1",c,t);q.Parameters.AddWithValue(cityId);return await q.ExecuteScalarAsync(ct)is not null;}

    WorldRoadDefinition? Road(int a,int b)=>content.WorldRoads.Values.FirstOrDefault(x=>x.Start==a&&x.End==b||x.Start==b&&x.End==a);
    int GeneralSpeed(int generalId)
    {
        if(!content.Generals.TryGetValue(generalId,out var general)||!content.Troops.TryGetValue(general.TroopId,out var troop))
            throw new GameException("WORLD_MOVE_SPEED_MISSING","General troop movement speed is missing.",500);
        return troop.Speed;
    }
    static TimeSpan MoveDuration(int length,int speed)
    {
        if(speed<=0)throw new GameException("WORLD_MOVE_SPEED_INVALID","Troop movement speed is invalid.",500);
        return TimeSpan.FromMilliseconds(Math.Max(0,(long)((double)length/speed*60_000d)/(DateTime.Now.Hour<8?3:2)/4L));
    }
    static AutoBattleView View(bool unlocked,Row? row)
    {
        if(row is null)return new(unlocked,0,0,0,0,0,0,0);
        var cd=row.State==1&&row.EndsAt.HasValue?Math.Max(0,(long)(row.EndsAt.Value-DateTimeOffset.UtcNow).TotalMilliseconds):0;
        return new(unlocked,row.State,row.TargetCityId,row.AutoType,cd,row.Exp,row.Lost,row.Result);
    }
    static bool IsTransient(string code)=>code is
        "WORLD_GENERAL_BUSY" or "WORLD_ALREADY_IN_CITY" or "WORLD_BATTLE_ACTIVE" or "WORLD_AUTO_NO_ROAD" or "WORLD_CITY_FOGGED" or
        "BATTLE_JOIN_DUPLICATE" or "BATTLE_ENDED" or "BATTLE_JOIN_GENERAL_INVALID" or "BATTLE_JOIN_FORBIDDEN" or
        "WORLD_REINFORCE_DUPLICATE" or "WORLD_REINFORCE_GENERAL_INVALID" or "WORLD_BATTLE_NOT_ACTIVE" or "WORLD_BATTLE_NOT_FOUND" or
        "WORLD_REINFORCE_FORCE_INVALID";
}
