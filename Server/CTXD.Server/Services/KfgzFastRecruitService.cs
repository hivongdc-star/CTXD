using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfgzFastRecruitResult(int GeneralId,int Forces,int MaxForces,int Healed,int RecruitTokenSpent,int GoldSpent,int FoodSpent,bool MubingActive);

public sealed class KfgzFastRecruitService(
    GameDb db,
    CanonicalContent content,
    ResourceProductionService production,
    TechnologyEffectService technologies,
    DstqActivityService dstq,
    GamePushHub push)
{
    const int FastRecruitChargeItemId=13;

    public async Task<KfgzFastRecruitResult> FastRecruitAsync(long playerId,int generalId,CancellationToken ct)
    {
        if(!content.ChargeItems.TryGetValue(FastRecruitChargeItemId,out var charge))
            throw new GameException("KFGZ_FAST_RECRUIT_STATIC_MISSING","Legacy fast recruit charge item 13 is missing.",500);

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);

        long seasonId,roundId;int deploymentState,forces;bool mubingActive;
        await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.id,d.state,pg.forces,d.mubing_active
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
            if(!await r.ReadAsync(ct))throw new GameException("KFGZ_FAST_RECRUIT_GENERAL_INVALID","General is not deployed in the active KFGZ round.",404);
            seasonId=r.GetInt64(0);roundId=r.GetInt64(1);deploymentState=r.GetInt16(2);forces=r.GetInt32(3);mubingActive=r.GetBoolean(4);
        }
        if(deploymentState!=1||mubingActive)
            throw new GameException("KFGZ_FAST_RECRUIT_GENERAL_BUSY","Legacy fast recruit requires an idle general that is not already recruiting.",409);

        await KfgzResourceLedger.RefreshSnapshotAsync(c,t,content,production,playerId,seasonId,ct);
        var max=await MaxForcesAsync(c,t,playerId,generalId,ct);
        var missing=Math.Max(0,max-forces);
        if(missing==0)throw new GameException("KFGZ_FAST_RECRUIT_ALREADY_FULL","General forces are already full.",409);

        int mubing,recruitTokens,userGold,sysGold;
        await using(var q=new NpgsqlCommand(@"
SELECT s.mubing,b.recruit_token,p.user_gold,p.sys_gold
FROM kfgz_signups s
JOIN player_battle_resources b ON b.player_id=s.player_id
JOIN players p ON p.id=s.player_id
WHERE s.season_id=$1 AND s.player_id=$2
FOR UPDATE OF s,b,p",c,t))
        {
            q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);
            await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
            mubing=Convert.ToInt32(r.GetValue(0));recruitTokens=Convert.ToInt32(r.GetValue(1));userGold=Convert.ToInt32(r.GetValue(2));sysGold=Convert.ToInt32(r.GetValue(3));
        }
        var output=mubing/3600d;
        if(output<=0)throw new GameException("KFGZ_FAST_RECRUIT_OUTPUT_ZERO","Legacy fast recruit requires positive recruitment output.",409);

        var needUnits=(int)Math.Ceiling(missing/(output*60d*5d));
        var tokenSpent=0;var goldSpent=0;var acceleratorUnits=needUnits;
        if(recruitTokens>=needUnits)tokenSpent=needUnits;
        else if(recruitTokens>0){tokenSpent=recruitTokens;acceleratorUnits=recruitTokens;}
        else goldSpent=needUnits;

        var healPotential=(int)(output*charge.Param*60d*acceleratorUnits);
        var healed=Math.Min(missing,Math.Max(0,healPotential));
        if(healed<=0)throw new GameException("KFGZ_FAST_RECRUIT_NO_EFFECT","Fast recruit produced no forces.",409);

        if(!content.Generals.TryGetValue(generalId,out var general))throw new GameException("KFGZ_FAST_RECRUIT_STATIC_MISSING","General static data is missing.",500);
        var foodPer=content.TroopConscripts.TryGetValue(general.TroopId,out var conscribe)?conscribe.Food:1d;
        var foodSpent=(int)(foodPer*healed);

        if(tokenSpent>0)
        {
            await using var use=new NpgsqlCommand("UPDATE player_battle_resources SET recruit_token=recruit_token-$2,updated_at=now() WHERE player_id=$1 AND recruit_token>=$2",c,t);
            use.Parameters.AddWithValue(playerId);use.Parameters.AddWithValue(tokenSpent);
            if(await use.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFGZ_FAST_RECRUIT_TOKEN_CHANGED","Recruit token balance changed; retry.",409);
            await KfgzResourceLedger.RecordDeltaAsync(c,t,seasonId,playerId,"recruitToken",-tokenSpent,"kfgz.fastRecruit",generalId,ct);
        }
        else if(goldSpent>0)
        {
            if((long)userGold+sysGold<goldSpent)throw new GameException("GOLD_NOT_ENOUGH","Not enough gold for fast recruit.");
            var useUser=Math.Min(userGold,goldSpent);var useSys=goldSpent-useUser;
            await using(var pay=new NpgsqlCommand("UPDATE players SET user_gold=user_gold-$2,sys_gold=sys_gold-$3,updated_at=now() WHERE id=$1",c,t))
            {pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(useUser);pay.Parameters.AddWithValue(useSys);await pay.ExecuteNonQueryAsync(ct);}
            await dstq.RecordGoldSpendAsync(c,t,playerId,goldSpent,ct);
            await KfgzResourceLedger.RecordDeltaAsync(c,t,seasonId,playerId,"gold",-goldSpent,"kfgz.fastRecruit",generalId,ct);
        }

        if(foodSpent>0)
        {
            await using var food=new NpgsqlCommand("UPDATE player_resources SET food=food-$2 WHERE player_id=$1 AND food>=$2",c,t);
            food.Parameters.AddWithValue(playerId);food.Parameters.AddWithValue(foodSpent);
            if(await food.ExecuteNonQueryAsync(ct)!=1)throw new GameException("FOOD_NOT_ENOUGH","Not enough food for fast recruit.");
            await KfgzResourceLedger.RecordDeltaAsync(c,t,seasonId,playerId,"food",-foodSpent,"kfgz.fastRecruit",generalId,ct);
        }

        var next=forces+healed;
        var continueMubing=next<max;
        await using(var save=new NpgsqlCommand(@"
UPDATE player_generals SET forces=$3,forces_updated_at=now(),updated_at=now() WHERE player_id=$1 AND general_id=$2;
UPDATE kfgz_deployments SET mubing_active=$4,mubing_updated_at=CASE WHEN $4 THEN now() ELSE NULL END,updated_at=now()
WHERE round_id=$5 AND player_id=$1 AND general_id=$2;",c,t))
        {
            save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(generalId);save.Parameters.AddWithValue(next);save.Parameters.AddWithValue(continueMubing);save.Parameters.AddWithValue(roundId);await save.ExecuteNonQueryAsync(ct);
        }
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"kfgz.general",new{reason="fastRecruit",generalId,forces=next,maxForces=max,healed,tokenSpent,goldSpent,foodSpent,mubingActive=continueMubing},ct);
        return new(generalId,next,max,healed,tokenSpent,goldSpent,foodSpent,continueMubing);
    }

    async Task<int> MaxForcesAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,CancellationToken ct)
    {
        int level;await using(var q=new NpgsqlCommand("SELECT level FROM player_generals WHERE player_id=$1 AND general_id=$2",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);level=Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
        int equipHp;await using(var q=new NpgsqlCommand("SELECT COALESCE(sum(CASE WHEN goods_type NOT IN(1,2,3,4,10,14) THEN attribute ELSE 0 END),0) FROM player_equipment WHERE player_id=$1 AND owner_general_id=$2",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);equipHp=checked((int)Convert.ToInt64(await q.ExecuteScalarAsync(ct)));}
        var techHp=await technologies.GetCompletedIntEffectAsync(playerId,30,2,ct,c,t);var columns=2+await technologies.GetCompletedIntEffectAsync(playerId,4,0,ct,c,t);
        var hp=(1200+(level-1)*24+equipHp+techHp)/3*Math.Max(1,columns)*3;return hp-hp%6;
    }
}
