using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfgzBattleResourceView(int RecruitToken,int Mubing,int PhantomCount);
public sealed record KfgzPhantomRequest(Guid RequestKey);
public sealed record KfgzPhantomResult(long BattleId,long PhantomUnitId,int GeneralId,bool UsedFree,int GoldCost,int PhantomCount);

public sealed class KfgzExtendedCombatService(
    GameDb db,
    CanonicalContent content,
    ResourceProductionService production,
    TechnologyEffectService technologies,
    ExperienceService experience,
    DstqActivityService dstq,
    GamePushHub push)
{
    const int PhantomChargeItemId = 53;

    public async Task<KfgzBattleResourceView> ResourcesAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        await EnsureBattleResourceAsync(c,t,playerId,ct);
        int recruitToken,phantomCount;
        await using(var q=new NpgsqlCommand("SELECT recruit_token,phantom_count FROM player_battle_resources WHERE player_id=$1",c,t))
        {
            q.Parameters.AddWithValue(playerId);
            await using var r=await q.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            recruitToken=r.GetInt32(0);
            phantomCount=r.GetInt32(1);
        }
        var perBuilding=await production.GetPerBuildingBaseOutputAsync(c,t,playerId,ct);
        var mubing=0;
        foreach(var pair in perBuilding)
            if(content.Buildings.TryGetValue(pair.Key,out var b)&&b.OutputType==5)
                mubing+=pair.Value;
        await t.CommitAsync(ct);
        return new(recruitToken,mubing,phantomCount);
    }

    public async Task<KfgzPhantomResult> CreatePhantomAsync(long playerId,long battleId,KfgzPhantomRequest request,CancellationToken ct)
    {
        if(request.RequestKey==Guid.Empty)throw new GameException("PHANTOM_REQUEST_KEY_REQUIRED","Phantom request key is required.");
        if(!content.ChargeItems.TryGetValue(PhantomChargeItemId,out var charge))
            throw new GameException("PHANTOM_STATIC_MISSING","Legacy phantom charge item 53 is missing.",500);

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);

        await using(var old=new NpgsqlCommand("SELECT phantom_unit_id,source_unit_id,used_free,gold_cost FROM battle_phantom_grants WHERE battle_id=$1 AND player_id=$2 AND request_key=$3",c,t))
        {
            old.Parameters.AddWithValue(battleId);old.Parameters.AddWithValue(playerId);old.Parameters.AddWithValue(request.RequestKey);
            await using var r=await old.ExecuteReaderAsync(ct);
            if(await r.ReadAsync(ct))
            {
                var phantomUnit=r.GetInt64(0);var sourceUnit=r.GetInt64(1);var usedFree=r.GetBoolean(2);var goldCost=r.GetInt32(3);
                int generalId;await using(var source=new NpgsqlCommand("SELECT general_id FROM battle_units WHERE id=$1",c,t)){source.Parameters.AddWithValue(sourceUnit);generalId=Convert.ToInt32(await source.ExecuteScalarAsync(ct));}
                int remaining;await using(var resource=new NpgsqlCommand("SELECT phantom_count FROM player_battle_resources WHERE player_id=$1",c,t)){resource.Parameters.AddWithValue(playerId);remaining=Convert.ToInt32(await resource.ExecuteScalarAsync(ct));}
                await t.CommitAsync(ct);
                return new(battleId,phantomUnit,generalId,usedFree,goldCost,remaining);
            }
        }

        int status,battleType;
        await using(var meta=new NpgsqlCommand("SELECT b.status,h.battle_type FROM battles b JOIN world_battle_handoffs h ON h.id=b.id WHERE b.id=$1 FOR UPDATE OF b,h",c,t))
        {
            meta.Parameters.AddWithValue(battleId);
            await using var r=await meta.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("BATTLE_NOT_FOUND","Battle does not exist.",404);
            status=r.GetInt16(0);battleType=r.GetInt16(1);
        }
        if(status!=0)throw new GameException("BATTLE_ENDED","Battle has ended.",409);
        if(battleType is not(3 or 14 or 18))throw new GameException("PHANTOM_BATTLE_TYPE_INVALID","Legacy phantom is only available in battle types 3, 14 and 18.",409);

        long? kfgzSeasonId=null;
        if(battleType==18)
        {
            await using var kfgz=new NpgsqlCommand("SELECT r.season_id FROM kfgz_battles kb JOIN kfgz_rounds r ON r.id=kb.round_id WHERE kb.battle_id=$1 AND kb.state=1 LIMIT 1",c,t);
            kfgz.Parameters.AddWithValue(battleId);
            var value=await kfgz.ExecuteScalarAsync(ct);
            if(value is not null)kfgzSeasonId=Convert.ToInt64(value);
            if(kfgzSeasonId.HasValue)await KfgzResourceLedger.RefreshSnapshotAsync(c,t,content,production,playerId,kfgzSeasonId.Value,ct);
        }

        var candidates=new List<(long id,int side,int general,int level)>();
        await using(var q=new NpgsqlCommand("SELECT id,side,general_id,level FROM battle_units WHERE battle_id=$1 AND player_id=$2 AND hp>0 AND detached=false AND is_phantom=false ORDER BY level DESC,sequence",c,t))
        {
            q.Parameters.AddWithValue(battleId);q.Parameters.AddWithValue(playerId);
            await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))candidates.Add((r.GetInt64(0),r.GetInt16(1),r.GetInt32(2),r.GetInt32(3)));
        }
        if(candidates.Count==0)throw new GameException("PHANTOM_PLAYER_NOT_IN_BATTLE","Player has no active real general in this battle.",403);
        var source=candidates.FirstOrDefault(x=>content.Generals.TryGetValue(x.general,out var g)&&g.Type!=4);
        if(source.id==0)throw new GameException("PHANTOM_SPECIAL_GENERAL_FORBIDDEN","Special general type 4 cannot be copied as a phantom.",409);

        int consumeLevel,userGold,sysGold;
        await using(var player=new NpgsqlCommand("SELECT consume_level,user_gold,sys_gold FROM players WHERE id=$1 FOR UPDATE",c,t))
        {
            player.Parameters.AddWithValue(playerId);
            await using var r=await player.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
            consumeLevel=r.GetInt32(0);userGold=r.GetInt32(1);sysGold=r.GetInt32(2);
        }
        if(consumeLevel<charge.Level)throw new GameException("PHANTOM_CONSUME_LEVEL_LOW","Consume level is below the legacy phantom requirement.",403);

        await EnsureBattleResourceAsync(c,t,playerId,ct);
        int free;
        await using(var resource=new NpgsqlCommand("SELECT phantom_count FROM player_battle_resources WHERE player_id=$1 FOR UPDATE",c,t))
        {resource.Parameters.AddWithValue(playerId);free=Convert.ToInt32(await resource.ExecuteScalarAsync(ct));}
        var usedFree=free>0;
        var goldCost=0;
        if(usedFree)
        {
            await using var use=new NpgsqlCommand("UPDATE player_battle_resources SET phantom_count=phantom_count-1,updated_at=now() WHERE player_id=$1",c,t);
            use.Parameters.AddWithValue(playerId);await use.ExecuteNonQueryAsync(ct);free--;
            if(kfgzSeasonId.HasValue)await KfgzResourceLedger.RecordDeltaAsync(c,t,kfgzSeasonId.Value,playerId,"phantomCount",-1,"kfgz.phantom",source.general,ct);
        }
        else
        {
            goldCost=charge.Cost;
            if((long)userGold+sysGold<goldCost)throw new GameException("GOLD_NOT_ENOUGH","Not enough gold to create a phantom.");
            var useUser=Math.Min(userGold,goldCost);var useSys=goldCost-useUser;
            await using(var pay=new NpgsqlCommand("UPDATE players SET user_gold=user_gold-$2,sys_gold=sys_gold-$3,updated_at=now() WHERE id=$1",c,t))
            {pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(useUser);pay.Parameters.AddWithValue(useSys);await pay.ExecuteNonQueryAsync(ct);}
            await dstq.RecordGoldSpendAsync(c,t,playerId,goldCost,ct);
            if(kfgzSeasonId.HasValue)await KfgzResourceLedger.RecordDeltaAsync(c,t,kfgzSeasonId.Value,playerId,"gold",-goldCost,"kfgz.phantom",source.general,ct);
            var exp=await technologies.GetCompletedIntEffectAsync(playerId,49,0,ct,c,t);
            if(exp>0)await experience.AddAsync(c,t,playerId,exp,ct);
        }

        int sequence;
        await using(var seq=new NpgsqlCommand("SELECT COALESCE(max(sequence),-1)+1 FROM battle_units WHERE battle_id=$1 AND side=$2",c,t))
        {seq.Parameters.AddWithValue(battleId);seq.Parameters.AddWithValue(source.side);sequence=Convert.ToInt32(await seq.ExecuteScalarAsync(ct));}

        long phantomId;
        await using(var clone=new NpgsqlCommand(@"INSERT INTO battle_units(
 battle_id,side,sequence,player_id,general_id,troop_id,name,level,attack,defense,leader,strength,hp,max_hp,is_npc,
 quality,tactic_id,tactic_damage,tactic_range,tactic_used,strategy_id,tech_tactic_attack,tech_tactic_defense,selected_action,event_npc_id,detached,is_phantom)
SELECT battle_id,side,$2,player_id,general_id,troop_id,name,level,attack,defense,leader,strength,hp,max_hp,false,
 quality,tactic_id,tactic_damage,tactic_range,false,strategy_id,tech_tactic_attack,tech_tactic_defense,2,NULL,false,true
FROM battle_units WHERE id=$1 RETURNING id",c,t))
        {clone.Parameters.AddWithValue(source.id);clone.Parameters.AddWithValue(sequence);phantomId=Convert.ToInt64(await clone.ExecuteScalarAsync(ct));}

        await using(var ledger=new NpgsqlCommand("INSERT INTO battle_phantom_grants(battle_id,player_id,request_key,source_unit_id,phantom_unit_id,used_free,gold_cost) VALUES($1,$2,$3,$4,$5,$6,$7)",c,t))
        {ledger.Parameters.AddWithValue(battleId);ledger.Parameters.AddWithValue(playerId);ledger.Parameters.AddWithValue(request.RequestKey);ledger.Parameters.AddWithValue(source.id);ledger.Parameters.AddWithValue(phantomId);ledger.Parameters.AddWithValue(usedFree);ledger.Parameters.AddWithValue(goldCost);await ledger.ExecuteNonQueryAsync(ct);}

        await t.CommitAsync(ct);
        await push.BroadcastAsync("battle.updated",new{battleId,reason="phantom",playerId,generalId=source.general,phantomUnitId=phantomId},ct);
        return new(battleId,phantomId,source.general,usedFree,goldCost,free);
    }

    static async Task EnsureBattleResourceAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("INSERT INTO player_battle_resources(player_id) VALUES($1) ON CONFLICT(player_id) DO NOTHING",c,t);
        q.Parameters.AddWithValue(playerId);await q.ExecuteNonQueryAsync(ct);
    }
}
