using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record GeneralTreasureView(long Id,int TreasureId,string Name,int Quality,int Lea,int Str,int OwnerGeneralId,bool Equipped,string Source,DateTimeOffset AcquiredAt);
public sealed record GeneralTreasureEquipRequest(int GeneralId);
public sealed record GeneralTreasureEffect(int Lea,int Str);

public sealed class GeneralTreasureService(GameDb db,GamePushHub push)
{
    sealed record Definition(int Id,string Name,int Quality,int MinGeneralLevel);

    // Authoritative GeneralTreasure rows from legacy sdata. KFZB uses only ids 4..6.
    static readonly IReadOnlyDictionary<int,Definition> Definitions=new Dictionary<int,Definition>
    {
        [4]=new(4,"和氏璧",4,35),
        [5]=new(5,"夜明珠",5,35),
        [6]=new(6,"紫珊瑚",6,35)
    };

    public async Task<GeneralTreasureView[]> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        var rows=new List<GeneralTreasureView>();
        await using var q=new NpgsqlCommand("SELECT id,treasure_id,lea,str,owner_general_id,state,source,acquired_at FROM player_general_treasures WHERE player_id=$1 ORDER BY id",c);
        q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))rows.Add(ToView(r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt16(5)==1,r.GetString(6),r.GetFieldValue<DateTimeOffset>(7)));
        return rows.ToArray();
    }

    public async Task<GeneralTreasureView> EquipAsync(long playerId,long instanceId,int generalId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var row=await LockAsync(c,t,playerId,instanceId,ct);
        if(row.equipped)
        {
            if(row.owner==generalId){await t.CommitAsync(ct);return ToView(instanceId,row.treasure,row.lea,row.str,row.owner,true,row.source,row.acquired);}
            throw new GameException("GENERAL_TREASURE_ALREADY_EQUIPPED","General Treasure is already equipped.",409);
        }
        var def=DefinitionOf(row.treasure);
        int level;await using(var q=new NpgsqlCommand("SELECT level FROM player_generals WHERE player_id=$1 AND general_id=$2 FOR UPDATE",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);var raw=await q.ExecuteScalarAsync(ct);if(raw is null)throw new GameException("GENERAL_TREASURE_GENERAL_MISSING","General does not exist for this player.",404);level=Convert.ToInt32(raw);}
        if(level<def.MinGeneralLevel)throw new GameException("GENERAL_TREASURE_GENERAL_LEVEL",$"General level {def.MinGeneralLevel} is required.",409);
        await using(var q=new NpgsqlCommand("UPDATE player_general_treasures SET owner_general_id=$3,state=1,updated_at=now() WHERE id=$1 AND player_id=$2",c,t)){q.Parameters.AddWithValue(instanceId);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await q.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);
        var view=ToView(instanceId,row.treasure,row.lea,row.str,generalId,true,row.source,row.acquired);await push.SendAsync(playerId,"general_treasure.updated",view,ct);return view;
    }

    public async Task<GeneralTreasureView> UnequipAsync(long playerId,long instanceId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var row=await LockAsync(c,t,playerId,instanceId,ct);
        if(row.equipped)await using(var q=new NpgsqlCommand("UPDATE player_general_treasures SET owner_general_id=0,state=0,updated_at=now() WHERE id=$1 AND player_id=$2",c,t)){q.Parameters.AddWithValue(instanceId);q.Parameters.AddWithValue(playerId);await q.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);
        var view=ToView(instanceId,row.treasure,row.lea,row.str,0,false,row.source,row.acquired);await push.SendAsync(playerId,"general_treasure.updated",view,ct);return view;
    }

    public static async Task<GeneralTreasureEffect> EffectAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT COALESCE(sum(lea),0)::int,COALESCE(sum(str),0)::int FROM player_general_treasures WHERE player_id=$1 AND owner_general_id=$2 AND state=1",c,t);
        q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);
        await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);return new(r.GetInt32(0),r.GetInt32(1));
    }

    public static async Task<(GeneralTreasureView view,bool created)> GrantAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int treasureId,int lea,int str,string source,string sourceKey,CancellationToken ct)
    {
        if(lea<0||str<0)throw new GameException("GENERAL_TREASURE_ATTRIBUTE_INVALID","General Treasure attributes are invalid.",500);
        _=DefinitionOf(treasureId);
        long id;bool created;DateTimeOffset acquired;
        await using(var q=new NpgsqlCommand("INSERT INTO player_general_treasures(player_id,treasure_id,lea,str,source,source_key) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(player_id,source_key) DO NOTHING RETURNING id,acquired_at",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(treasureId);q.Parameters.AddWithValue(lea);q.Parameters.AddWithValue(str);q.Parameters.AddWithValue(source);q.Parameters.AddWithValue(sourceKey);
            await using var r=await q.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){id=r.GetInt64(0);acquired=r.GetFieldValue<DateTimeOffset>(1);created=true;}else{id=0;acquired=default;created=false;}
        }
        if(!created)
        {
            await using var q=new NpgsqlCommand("SELECT id,treasure_id,lea,str,owner_general_id,state,source,acquired_at FROM player_general_treasures WHERE player_id=$1 AND source_key=$2",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(sourceKey);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("GENERAL_TREASURE_GRANT_CONFLICT","General Treasure idempotency row disappeared.",500);var view=ToView(r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt16(5)==1,r.GetString(6),r.GetFieldValue<DateTimeOffset>(7));if(view.TreasureId!=treasureId||view.Lea!=lea||view.Str!=str)throw new GameException("GENERAL_TREASURE_GRANT_CONFLICT","General Treasure idempotency key maps to different attributes.",500);return(view,false);
        }
        return(ToView(id,treasureId,lea,str,0,false,source,acquired),true);
    }

    public static async Task<GeneralTreasureView?> FindBySourceKeyAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,string sourceKey,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT id,treasure_id,lea,str,owner_general_id,state,source,acquired_at FROM player_general_treasures WHERE player_id=$1 AND source_key=$2",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(sourceKey);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;return ToView(r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt16(5)==1,r.GetString(6),r.GetFieldValue<DateTimeOffset>(7));
    }

    static async Task<(int treasure,int lea,int str,int owner,bool equipped,string source,DateTimeOffset acquired)> LockAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,long instanceId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT treasure_id,lea,str,owner_general_id,state,source,acquired_at FROM player_general_treasures WHERE id=$1 AND player_id=$2 FOR UPDATE",c,t);q.Parameters.AddWithValue(instanceId);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("GENERAL_TREASURE_NOT_FOUND","General Treasure does not exist for this player.",404);return(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt16(4)==1,r.GetString(5),r.GetFieldValue<DateTimeOffset>(6));
    }

    static Definition DefinitionOf(int treasureId)=>Definitions.TryGetValue(treasureId,out var def)?def:throw new GameException("GENERAL_TREASURE_STATIC_MISSING",$"General Treasure {treasureId} is not available in the authoritative remake slice.",500);
    static GeneralTreasureView ToView(long id,int treasureId,int lea,int str,int owner,bool equipped,string source,DateTimeOffset acquired){var d=DefinitionOf(treasureId);return new(id,treasureId,d.Name,d.Quality,lea,str,owner,equipped,source,acquired);}
}
