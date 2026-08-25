using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class PlayerQueryService(GameDb db)
{
    public async Task<PlayerView> GetPlayerAsync(long playerId,CancellationToken ct,NpgsqlConnection? existing=null,NpgsqlTransaction? tx=null)
    {
        var own=existing is null; var conn=existing ?? await db.DataSource.OpenConnectionAsync(ct);
        try {
            long id; string? name; int pic,force,level,currentTask,slots,free,consume; long exp,sysGold,userGold; bool canName;
            await using(var cmd=new NpgsqlCommand(@"SELECT p.id,p.display_name,p.pic,p.force_id,p.level,p.exp,p.current_task_id,
EXISTS(SELECT 1 FROM player_functions f WHERE f.player_id=p.id AND f.function_id=3),
p.construction_slots,p.free_construction_num,p.sys_gold,p.user_gold,p.consume_level
FROM players p WHERE p.id=$1",conn,tx))
            {
                cmd.Parameters.AddWithValue(playerId); await using var r=await cmd.ExecuteReaderAsync(ct);
                if(!await r.ReadAsync(ct)) throw new GameException("PLAYER_NOT_FOUND","Không tìm thấy nhân vật.",404);
                id=r.GetInt64(0); name=r.IsDBNull(1)?null:r.GetString(1); pic=r.GetInt32(2); force=r.GetInt16(3); level=r.GetInt32(4);
                exp=r.GetInt64(5); currentTask=r.GetInt32(6); canName=r.GetBoolean(7); slots=r.GetInt32(8); free=r.GetInt32(9);
                sysGold=r.GetInt64(10); userGold=r.GetInt64(11); consume=r.GetInt32(12);
            }
            var functions=new List<int>();
            await using(var cmd=new NpgsqlCommand("SELECT function_id FROM player_functions WHERE player_id=$1 ORDER BY function_id",conn,tx))
            {
                cmd.Parameters.AddWithValue(playerId); await using var r=await cmd.ExecuteReaderAsync(ct);
                while(await r.ReadAsync(ct)) functions.Add(r.GetInt32(0));
            }
            return new PlayerView(id,name,pic,force,level,exp,currentTask,canName,slots,free,sysGold,userGold,consume,functions);
        } finally { if(own) await conn.DisposeAsync(); }
    }
}
