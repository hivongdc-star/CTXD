using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfgzMubingResult(int GeneralId,bool Active,int Forces,int MaxForces,int Mubing,int Food);

public sealed class KfgzMubingService(
    GameDb db,
    CanonicalContent content,
    ResourceProductionService production,
    TechnologyEffectService technologies,
    GamePushHub push)
{
    public async Task<KfgzMubingResult> StartAsync(long playerId,int generalId,CancellationToken ct)
    {
        await TickPlayerAsync(playerId,ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);

        long seasonId,roundId;
        int state,forces;
        await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.id,d.state,pg.forces
FROM kfgz_rounds r
JOIN kfgz_signups s ON s.season_id=r.season_id AND s.player_id=$1
JOIN kfgz_deployments d ON d.round_id=r.id AND d.player_id=$1 AND d.general_id=$2
JOIN player_generals pg ON pg.player_id=$1 AND pg.general_id=$2
WHERE r.state=1
ORDER BY r.round_no DESC
LIMIT 1
FOR UPDATE OF d,pg,s",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);
            await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("KFGZ_MUBING_GENERAL_INVALID","General is not deployed in the active KFGZ round.",404);
            seasonId=r.GetInt64(0);roundId=r.GetInt64(1);state=r.GetInt16(2);forces=r.GetInt32(3);
        }
        if(state!=1)throw new GameException("KFGZ_MUBING_GENERAL_BUSY","Legacy mubing can only start while the general is idle in a KFGZ city.",409);

        await KfgzResourceLedger.RefreshSnapshotAsync(c,t,db,content,production,playerId,seasonId,ct);
        var max=await MaxForcesAsync(c,t,playerId,generalId,ct);
        if(forces>=max)throw new GameException("KFGZ_MUBING_ALREADY_FULL","General forces are already full.",409);

        int mubing,food;
        await using(var res=new NpgsqlCommand("SELECT mubing,food FROM kfgz_signups WHERE season_id=$1 AND player_id=$2",c,t))
        {res.Parameters.AddWithValue(seasonId);res.Parameters.AddWithValue(playerId);await using var r=await res.ExecuteReaderAsync(ct);await r.ReadAsync(ct);mubing=r.GetInt32(0);food=checked((int)Math.Min(int.MaxValue,r.GetInt64(1)));}

        await using(var start=new NpgsqlCommand("UPDATE kfgz_deployments SET mubing_active=true,mubing_updated_at=now(),updated_at=now() WHERE round_id=$1 AND player_id=$2 AND general_id=$3",c,t))
        {start.Parameters.AddWithValue(roundId);start.Parameters.AddWithValue(playerId);start.Parameters.AddWithValue(generalId);await start.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"kfgz.general",new{reason="mubing.start",generalId,forces,maxForces=max,mubing},ct);
        return new(generalId,true,forces,max,mubing,food);
    }

    public async Task TickPlayerAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var rows=new List<(long season,long round,int general,DateTimeOffset updated,int forces)>();
        await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.id,d.general_id,d.mubing_updated_at,pg.forces
FROM kfgz_rounds r
JOIN kfgz_deployments d ON d.round_id=r.id AND d.player_id=$1
JOIN player_generals pg ON pg.player_id=d.player_id AND pg.general_id=d.general_id
WHERE r.state=1 AND d.state=1 AND d.mubing_active=true AND d.mubing_updated_at IS NOT NULL
ORDER BY d.general_id
FOR UPDATE OF d,pg",c,t))
        {
            q.Parameters.AddWithValue(playerId);
            await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))rows.Add((r.GetInt64(0),r.GetInt64(1),r.GetInt32(2),r.GetFieldValue<DateTimeOffset>(3),r.GetInt32(4)));
        }
        if(rows.Count==0){await t.CommitAsync(ct);return;}

        var seasonId=rows[0].season;
        await KfgzResourceLedger.RefreshSnapshotAsync(c,t,db,content,production,playerId,seasonId,ct);
        int mubing;
        await using(var q=new NpgsqlCommand("SELECT mubing FROM kfgz_signups WHERE season_id=$1 AND player_id=$2 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);mubing=Convert.ToInt32(await q.ExecuteScalarAsync(ct)??0);}

        var now=DateTimeOffset.UtcNow;var changed=new List<object>();
        foreach(var row in rows)
        {
            var elapsed=Math.Max(0,(int)(now-row.updated).TotalSeconds);
            await using(var stamp=new NpgsqlCommand("UPDATE kfgz_deployments SET mubing_updated_at=$4,updated_at=now() WHERE round_id=$1 AND player_id=$2 AND general_id=$3",c,t))
            {stamp.Parameters.AddWithValue(row.round);stamp.Parameters.AddWithValue(playerId);stamp.Parameters.AddWithValue(row.general);stamp.Parameters.AddWithValue(now);await stamp.ExecuteNonQueryAsync(ct);}
            if(elapsed<=0||mubing<=0)continue;

            var max=await MaxForcesAsync(c,t,playerId,row.general,ct);
            var need=Math.Max(0,max-row.forces);
            if(need==0){await StopAsync(c,t,row.round,playerId,row.general,ct);continue;}
            var heal=Math.Min(need,(int)(mubing/3600d*elapsed));
            if(heal<=0)continue;

            if(!content.Generals.TryGetValue(row.general,out var g))throw new GameException("KFGZ_MUBING_STATIC_MISSING","General static data is missing.",500);
            var foodPer=content.TroopConscripts.TryGetValue(g.TroopId,out var conscribe)?conscribe.Food:1d;
            var cost=(int)(foodPer*heal);
            if(cost>0)
            {
                await using var pay=new NpgsqlCommand("UPDATE player_resources SET food=food-$2 WHERE player_id=$1 AND food>=$2",c,t);
                pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(cost);
                if(await pay.ExecuteNonQueryAsync(ct)!=1){await StopAsync(c,t,row.round,playerId,row.general,ct);continue;}
                await KfgzResourceLedger.RecordDeltaAsync(c,t,seasonId,playerId,"food",-cost,"kfgz.mubing",row.general,ct);
            }

            var next=row.forces+heal;
            await using(var save=new NpgsqlCommand("UPDATE player_generals SET forces=$3,forces_updated_at=now(),updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t))
            {save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(row.general);save.Parameters.AddWithValue(next);await save.ExecuteNonQueryAsync(ct);}
            if(next>=max)await StopAsync(c,t,row.round,playerId,row.general,ct);
            changed.Add(new{generalId=row.general,forces=next,maxForces=max,healed=heal,foodCost=cost});
        }
        await t.CommitAsync(ct);
        if(changed.Count>0)await push.SendAsync(playerId,"kfgz.general",new{reason="mubing.tick",items=changed},ct);
    }

    async Task<int> MaxForcesAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,CancellationToken ct)
    {
        int level;
        await using(var q=new NpgsqlCommand("SELECT level FROM player_generals WHERE player_id=$1 AND general_id=$2",c,t))
        {q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);level=Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
        var equipHp=0;
        await using(var q=new NpgsqlCommand("SELECT COALESCE(sum(CASE WHEN goods_type NOT IN(1,2,3,4,10,14) THEN attribute ELSE 0 END),0) FROM player_equipment WHERE player_id=$1 AND owner_general_id=$2",c,t))
        {q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);equipHp=checked((int)Convert.ToInt64(await q.ExecuteScalarAsync(ct)));}
        var techHp=await technologies.GetCompletedIntEffectAsync(playerId,30,2,ct,c,t);
        var columns=2+await technologies.GetCompletedIntEffectAsync(playerId,4,0,ct,c,t);
        var hp=(1200+(level-1)*24+equipHp+techHp)/3*Math.Max(1,columns)*3;
        return hp-hp%6;
    }

    static async Task StopAsync(NpgsqlConnection c,NpgsqlTransaction t,long round,long player,int general,CancellationToken ct)
    {await using var q=new NpgsqlCommand("UPDATE kfgz_deployments SET mubing_active=false,updated_at=now() WHERE round_id=$1 AND player_id=$2 AND general_id=$3",c,t);q.Parameters.AddWithValue(round);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(general);await q.ExecuteNonQueryAsync(ct);}
}

internal static class KfgzResourceLedger
{
    public static async Task RefreshSnapshotAsync(NpgsqlConnection c,NpgsqlTransaction t,GameDb db,CanonicalContent content,ResourceProductionService production,long playerId,long seasonId,CancellationToken ct)
    {
        await using(var ensure=new NpgsqlCommand("INSERT INTO player_battle_resources(player_id) VALUES($1) ON CONFLICT(player_id) DO NOTHING",c,t)){ensure.Parameters.AddWithValue(playerId);await ensure.ExecuteNonQueryAsync(ct);}
        long gold,copper,wood,food,iron;int recruit,phantom;
        await using(var q=new NpgsqlCommand("SELECT p.sys_gold,r.copper,r.wood,r.food,r.iron,b.recruit_token,b.phantom_count FROM players p JOIN player_resources r ON r.player_id=p.id JOIN player_battle_resources b ON b.player_id=p.id WHERE p.id=$1",c,t))
        {q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player resources do not exist.",404);gold=r.GetInt64(0);copper=r.GetInt64(1);wood=r.GetInt64(2);food=r.GetInt64(3);iron=r.GetInt64(4);recruit=r.GetInt32(5);phantom=r.GetInt32(6);}
        var perBuilding=await production.GetPerBuildingBaseOutputAsync(c,t,playerId,ct);var mubing=0;
        foreach(var pair in perBuilding)if(content.Buildings.TryGetValue(pair.Key,out var b)&&b.OutputType==5)mubing+=pair.Value;
        await using var save=new NpgsqlCommand("UPDATE kfgz_signups SET sys_gold=$3,copper=$4,wood=$5,food=$6,iron=$7,recruit_token=$8,mubing=$9,phantom_count=$10,synced_at=now() WHERE season_id=$1 AND player_id=$2",c,t);
        save.Parameters.AddWithValue(seasonId);save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(gold);save.Parameters.AddWithValue(copper);save.Parameters.AddWithValue(wood);save.Parameters.AddWithValue(food);save.Parameters.AddWithValue(iron);save.Parameters.AddWithValue(recruit);save.Parameters.AddWithValue(mubing);save.Parameters.AddWithValue(phantom);await save.ExecuteNonQueryAsync(ct);
    }

    public static async Task<long> RecordDeltaAsync(NpgsqlConnection c,NpgsqlTransaction t,long seasonId,long playerId,string unit,long delta,string reason,int? generalId,CancellationToken ct)
    {
        long id;await using(var q=new NpgsqlCommand("INSERT INTO kfgz_resource_changes(season_id,player_id,unit,delta,reason,general_id) VALUES($1,$2,$3,$4,$5,$6) RETURNING id",c,t))
        {q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(unit);q.Parameters.AddWithValue(delta);q.Parameters.AddWithValue(reason);q.Parameters.AddWithValue((object?)generalId??DBNull.Value);id=Convert.ToInt64(await q.ExecuteScalarAsync(ct));}
        var column=unit.ToLowerInvariant() switch{"gold"=>"sys_gold","copper"=>"copper","wood"=>"wood","food"=>"food","iron"=>"iron","recruittoken"=>"recruit_token","phantomcount"=>"phantom_count",_=>throw new InvalidOperationException($"Unsupported KFGZ resource unit {unit}")};
        await using var save=new NpgsqlCommand($"UPDATE kfgz_signups SET {column}=GREATEST(0,{column}+$3),resource_version=$4 WHERE season_id=$1 AND player_id=$2",c,t);
        save.Parameters.AddWithValue(seasonId);save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(delta);save.Parameters.AddWithValue(id);await save.ExecuteNonQueryAsync(ct);return id;
    }
}
