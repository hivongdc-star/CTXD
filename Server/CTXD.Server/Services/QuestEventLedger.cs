using CTXD.Server.Data;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public static class QuestEventLedger
{
    public static async Task RecordAsync(GameDb db,long playerId,string kind,int arg,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await RecordAsync(c,null,playerId,kind,arg,ct);
    }

    public static async Task RecordAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,string kind,int arg,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand(@"INSERT INTO player_quest_events(player_id,kind,arg,count)
VALUES($1,$2,$3,1)
ON CONFLICT(player_id,kind,arg) DO UPDATE SET count=player_quest_events.count+1,updated_at=now()",c,t);
        cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(kind);cmd.Parameters.AddWithValue(arg);await cmd.ExecuteNonQueryAsync(ct);
    }

    static string Scoped(string kind,int taskId)=>$"{kind}:{taskId}";

    public static async Task RecordCurrentAsync(GameDb db,long playerId,string kind,int arg,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await RecordCurrentAsync(c,null,playerId,kind,arg,ct);
    }

    public static async Task RecordCurrentAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,string kind,int arg,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT current_task_id FROM players WHERE id=$1",c,t);q.Parameters.AddWithValue(playerId);var value=await q.ExecuteScalarAsync(ct);if(value is null)return;await RecordAsync(c,t,playerId,Scoped(kind,Convert.ToInt32(value)),arg,ct);
    }

    public static Task<int> CountForTaskAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,int taskId,string kind,int? arg,CancellationToken ct)=>CountAsync(c,t,playerId,Scoped(kind,taskId),arg,ct);

    public static async Task<int> CountAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,string kind,int? arg,CancellationToken ct)
    {
        var sql=arg.HasValue
            ?"SELECT COALESCE((SELECT count FROM player_quest_events WHERE player_id=$1 AND kind=$2 AND arg=$3),0)"
            :"SELECT COALESCE(sum(count),0) FROM player_quest_events WHERE player_id=$1 AND kind=$2";
        await using var cmd=new NpgsqlCommand(sql,c,t);cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(kind);if(arg.HasValue)cmd.Parameters.AddWithValue(arg.Value);return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }
}
