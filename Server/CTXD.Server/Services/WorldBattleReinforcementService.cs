using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class WorldBattleReinforcementService(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technologies,
    BattleService battles)
{
    public async Task<BattleView> JoinAsync(long playerId,long battleId,int generalId,CancellationToken ct)
    {
        long leadPlayer;
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT attacker_player_id FROM world_battle_handoffs WHERE id=$1 AND status=0",c))
        {
            q.Parameters.AddWithValue(battleId);
            var value=await q.ExecuteScalarAsync(ct);
            if(value is null)throw new GameException("WORLD_BATTLE_NOT_ACTIVE","World battle is not active.",404);
            leadPlayer=Convert.ToInt64(value);
        }

        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var exists=new NpgsqlCommand("SELECT 1 FROM battles WHERE id=$1",c))
        {
            exists.Parameters.AddWithValue(battleId);
            if(await exists.ExecuteScalarAsync(ct)is null)await battles.GetAsync(leadPlayer,battleId,ct);
        }

        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            int cityId,battleType,status,side;
            await using(var meta=new NpgsqlCommand(@"
SELECT h.city_id,h.battle_type,b.status,
       CASE WHEN p.force_id=h.attacker_force_id THEN 1 WHEN p.force_id=h.defender_force_id THEN 2 ELSE 0 END
FROM world_battle_handoffs h
JOIN battles b ON b.id=h.id
JOIN players p ON p.id=$2
WHERE h.id=$1
FOR UPDATE OF h,b",c,t))
            {
                meta.Parameters.AddWithValue(battleId);meta.Parameters.AddWithValue(playerId);
                await using var r=await meta.ExecuteReaderAsync(ct);
                if(!await r.ReadAsync(ct))throw new GameException("WORLD_BATTLE_NOT_FOUND","World battle does not exist.",404);
                cityId=r.GetInt32(0);battleType=r.GetInt16(1);status=r.GetInt16(2);side=r.GetInt32(3);
            }
            if(status!=0)throw new GameException("BATTLE_ENDED","Battle has ended.",409);
            if(battleType is not(3 or 14))throw new GameException("WORLD_REINFORCE_TYPE_INVALID","Only legacy world city battles accept this reinforcement path.",409);
            if(side is not(1 or 2))throw new GameException("WORLD_REINFORCE_FORCE_INVALID","Player force is not a participant in this world battle.",403);

            await using(var check=new NpgsqlCommand("SELECT 1 FROM player_generals WHERE player_id=$1 AND general_id=$2 AND general_type=2 AND state<=1 AND forces>0 AND location_id=$3 FOR UPDATE",c,t))
            {
                check.Parameters.AddWithValue(playerId);check.Parameters.AddWithValue(generalId);check.Parameters.AddWithValue(cityId);
                if(await check.ExecuteScalarAsync(ct)is null)throw new GameException("WORLD_REINFORCE_GENERAL_INVALID","General must be ready in the battle city.",409);
            }
            await using(var duplicate=new NpgsqlCommand("SELECT 1 FROM battle_units WHERE battle_id=$1 AND player_id=$2 AND general_id=$3 AND is_phantom=false",c,t))
            {
                duplicate.Parameters.AddWithValue(battleId);duplicate.Parameters.AddWithValue(playerId);duplicate.Parameters.AddWithValue(generalId);
                if(await duplicate.ExecuteScalarAsync(ct)is not null)throw new GameException("WORLD_REINFORCE_DUPLICATE","General already participates in this battle.",409);
            }

            int sequence;
            await using(var seq=new NpgsqlCommand("SELECT COALESCE(max(sequence),-1)+1 FROM battle_units WHERE battle_id=$1 AND side=$2",c,t))
            {seq.Parameters.AddWithValue(battleId);seq.Parameters.AddWithValue(side);sequence=Convert.ToInt32(await seq.ExecuteScalarAsync(ct));}
            await AddPlayerUnitAsync(c,t,battleId,side,sequence,playerId,generalId,cityId,ct);
            await using(var state=new NpgsqlCommand("UPDATE player_generals SET state=3,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t))
            {state.Parameters.AddWithValue(playerId);state.Parameters.AddWithValue(generalId);await state.ExecuteNonQueryAsync(ct);}
            await t.CommitAsync(ct);
        }
        return await battles.GetAsync(playerId,battleId,ct);
    }

    async Task AddPlayerUnitAsync(NpgsqlConnection c,NpgsqlTransaction t,long battleId,int side,int sequence,long playerId,int generalId,int cityId,CancellationToken ct)
    {
        int level,forces,leaderBonus,strengthBonus;
        await using(var q=new NpgsqlCommand("SELECT level,forces,leader_bonus,strength_bonus FROM player_generals WHERE player_id=$1 AND general_id=$2 FOR UPDATE",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("WORLD_REINFORCE_GENERAL_MISSING","General does not exist.",404);
            level=r.GetInt32(0);forces=r.GetInt32(1);leaderBonus=r.GetInt32(2);strengthBonus=r.GetInt32(3);
        }
        if(!content.Generals.TryGetValue(generalId,out var general)||!content.Troops.TryGetValue(general.TroopId,out var troop))
            throw new GameException("WORLD_REINFORCE_STATIC_MISSING","General troop data is missing.",500);
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
        var terrain=content.WorldCities.TryGetValue(cityId,out var city)?city.Terrain:0;
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
            var p=group.Split('|');if(p.Length<2||!int.TryParse(p[0],out var id)||id!=terrain)continue;
            return p[1].Split(',').Select(x=>int.TryParse(x,out var value)?value:0).Where(x=>x!=0).Distinct().ToArray();
        }
        return [];
    }
}
