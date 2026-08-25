using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class PlayerFlowService(GameDb db,PlayerQueryService query,TutorialService tutorial,LegacyNameService names)
{
    public async Task<PlayerView> ChooseForceAsync(long playerId,int forceId,CancellationToken ct)
    {
        if(forceId is <1 or >3) throw new GameException("FORCE_INVALID","Phe không hợp lệ.");
        await using var conn=await db.DataSource.OpenConnectionAsync(ct); await using var tx=await conn.BeginTransactionAsync(ct);
        int current;
        await using(var read=new NpgsqlCommand("SELECT force_id FROM players WHERE id=$1 FOR UPDATE",conn,tx)){read.Parameters.AddWithValue(playerId);current=Convert.ToInt32(await read.ExecuteScalarAsync(ct));}
        if(current!=0) throw new GameException("FORCE_ALREADY_SET","Đã chọn phe.");
        await using(var cmd=new NpgsqlCommand("UPDATE players SET force_id=$2,updated_at=now() WHERE id=$1",conn,tx)){cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(forceId);await cmd.ExecuteNonQueryAsync(ct);}
        await tutorial.TryCompleteAsync(conn,tx,playerId,"chose_side",[],ct);
        await tx.CommitAsync(ct); return await query.GetPlayerAsync(playerId,ct);
    }

    public Task<IReadOnlyList<string>> RandomNamesAsync(bool male,int count,CancellationToken ct) => names.GenerateAsync(male,Math.Clamp(count,1,5),ct);

    public async Task<PlayerView> SetNameAndPicAsync(long playerId,string name,int pic,CancellationToken ct)
    {
        name=(name??"").Trim(); if(!names.IsFormatValid(name)) throw new GameException("NAME_INVALID","Tên không hợp lệ hoặc dài quá 7 ký tự.");
        await using var conn=await db.DataSource.OpenConnectionAsync(ct); await using var tx=await conn.BeginTransactionAsync(ct);
        bool can;
        await using(var cmd=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=3)",conn,tx)){cmd.Parameters.AddWithValue(playerId);can=(bool)(await cmd.ExecuteScalarAsync(ct))!;}
        if(!can) throw new GameException("NAME_LOCKED","Chưa mở chức năng đặt tên.");
        try {
            await using(var cmd=new NpgsqlCommand("UPDATE players SET display_name=$2,pic=$3,updated_at=now() WHERE id=$1",conn,tx)){cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(name);cmd.Parameters.AddWithValue(pic);await cmd.ExecuteNonQueryAsync(ct);}
        } catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation) { throw new GameException("NAME_EXISTS","Tên đã được sử dụng."); }
        await tutorial.TryCompleteAsync(conn,tx,playerId,"change_name",[],ct);
        await tx.CommitAsync(ct); return await query.GetPlayerAsync(playerId,ct);
    }
}
