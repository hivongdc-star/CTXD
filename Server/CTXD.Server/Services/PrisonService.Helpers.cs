using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed partial class PrisonService
{
    static int EffectiveLashLevel(Holder holder)=>Math.Min(5,holder.LashLv+(holder.ExpireAt>DateTimeOffset.UtcNow?1:0));

    async Task<int> TryAddPointAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,Holder holder,CancellationToken ct)
    {
        var degree=data.Degrees[holder.LashLv];if(degree.ExpFree<=0||holder.Point>=degree.ExpFree)return holder.Point;
        if(await technologies.GetCompletedIntEffectAsync(playerId,58,0,ct,c,t)<=0)return holder.Point;
        if(Random.Shared.NextDouble()>=degree.GetExpProb)return holder.Point;
        var point=holder.Point+1;var lash=holder.LashLv;
        await using(var add=new NpgsqlCommand("UPDATE player_prisons SET point=point+1,updated_at=now() WHERE player_id=$1",c,t)){add.Parameters.AddWithValue(playerId);await add.ExecuteNonQueryAsync(ct);}
        if(lash<data.Degrees.Count&&point+holder.TrailGold>=data.Degrees[lash+1].Cost)
        {
            lash++;
            await using var up=new NpgsqlCommand("UPDATE player_prisons SET lash_lv=$2,expire_at=NULL,trail_gold=0,updated_at=now() WHERE player_id=$1",c,t);up.Parameters.AddWithValue(playerId);up.Parameters.AddWithValue(lash);await up.ExecuteNonQueryAsync(ct);
            await slaveActivity.UnlockAsync(c,t,playerId,holder.LashLv,ct);
        }
        return point;
    }

    int RewardExp(int prisonLv,int generalLv,int official)
    {
        var level=data.LashRewards.FirstOrDefault(x=>x.Type==1&&prisonLv>=x.PrisonLowLv&&prisonLv<=x.PrisonHighLv&&generalLv>=x.LowLv&&generalLv<=x.HighLv)?.ExpReward??0;
        if(prisonLv<3||official<=0)return level;
        return level+(data.LashRewards.FirstOrDefault(x=>x.Type==2&&prisonLv>=x.PrisonLowLv&&prisonLv<=x.PrisonHighLv&&official>=x.OfficialLow&&official<=x.OfficialHigh)?.ExpReward??0);
    }
    PrisonCatchDef? CatchRow(int n,int prisonLv)=>data.CatchRows.FirstOrDefault(x=>x.N==n&&prisonLv>=x.PrisonLowLv&&prisonLv<=x.PrisonHighLv);
    PrisonDegreeView[] Degrees()=>data.Degrees.Values.OrderBy(x=>x.Degree).Select(x=>new PrisonDegreeView(x.Degree,x.ExpExtra,x.TimeExtra,x.Cost,x.ExpFree,x.ExpSum)).ToArray();
    static bool CanUpdatePrison(int prisonLv,int playerLv)=>prisonLv switch{1=>playerLv>=83,2=>playerLv>=85,3=>playerLv>=87,4=>playerLv>=89,_=>false};

    async Task<PrisonerView[]> ReadPrisonersAsync(NpgsqlConnection c,long holder,CancellationToken ct)
    {
        var result=new List<PrisonerView>();await using var q=new NpgsqlCommand("SELECT s.id,s.slave_player_id,s.general_id,COALESCE(p.display_name,''),s.force_id,g.level,s.slash_times,s.grab_time,s.escape_at FROM player_slaves s LEFT JOIN players p ON p.id=s.slave_player_id LEFT JOIN player_generals g ON g.player_id=s.slave_player_id AND g.general_id=s.general_id WHERE s.holder_player_id=$1 AND s.type=1 ORDER BY s.grab_time",c);q.Parameters.AddWithValue(holder);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var generalId=r.GetInt32(2);content.Generals.TryGetValue(generalId,out var def);result.Add(new(r.GetInt64(0),r.GetInt64(1),generalId,r.GetString(3),def?.Name??$"General {generalId}",def?.Pic??"",r.GetInt16(4),r.IsDBNull(5)?0:r.GetInt32(5),r.GetInt32(6),r.GetFieldValue<DateTimeOffset>(7),r.IsDBNull(8)?null:r.GetFieldValue<DateTimeOffset>(8)));}return result.ToArray();
    }
    async Task<CaptiveGeneralView[]> ReadCaptivesAsync(NpgsqlConnection c,long player,CancellationToken ct)
    {
        var result=new List<CaptiveGeneralView>();await using var q=new NpgsqlCommand("SELECT s.id,s.holder_player_id,COALESCE(p.display_name,''),s.general_id,s.slash_times,s.grab_time,s.escape_at FROM player_slaves s LEFT JOIN players p ON p.id=s.holder_player_id WHERE s.slave_player_id=$1 AND s.type=1 ORDER BY s.grab_time",c);q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var generalId=r.GetInt32(3);content.Generals.TryGetValue(generalId,out var def);result.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),generalId,def?.Name??$"General {generalId}",r.GetInt32(4),r.GetFieldValue<DateTimeOffset>(5),r.IsDBNull(6)?null:r.GetFieldValue<DateTimeOffset>(6)));}return result.ToArray();
    }

    static async Task DeleteSlaveAsync(NpgsqlConnection c,NpgsqlTransaction t,long id,long slavePlayer,int general,CancellationToken ct)
    {
        await using(var del=new NpgsqlCommand("DELETE FROM player_slaves WHERE id=$1",c,t)){del.Parameters.AddWithValue(id);await del.ExecuteNonQueryAsync(ct);}
        await using(var state=new NpgsqlCommand("UPDATE player_generals SET state=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2 AND state IN(22,23)",c,t)){state.Parameters.AddWithValue(slavePlayer);state.Parameters.AddWithValue(general);state.Parameters.AddWithValue(IdleState);await state.ExecuteNonQueryAsync(ct);}
    }
    static async Task MarkCaptureAsync(NpgsqlConnection c,NpgsqlTransaction t,long battle,long unit,double probability,bool captured,CancellationToken ct){await using var q=new NpgsqlCommand("UPDATE prison_capture_attempts SET probability=$3,captured=$4,processed_at=now() WHERE battle_id=$1 AND killed_unit_id=$2",c,t);q.Parameters.AddWithValue(battle);q.Parameters.AddWithValue(unit);q.Parameters.AddWithValue(probability);q.Parameters.AddWithValue(captured);await q.ExecuteNonQueryAsync(ct);}

    static async Task<long> ItemOwnedAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,int item,int type,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT COALESCE((SELECT quantity FROM player_items WHERE player_id=$1 AND item_id=$2 AND item_type=$3),0)",c,t);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(item);q.Parameters.AddWithValue(type);return Convert.ToInt64(await q.ExecuteScalarAsync(ct));}
    static async Task<int> PlayerLevelAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,bool update,CancellationToken ct){await using var q=new NpgsqlCommand($"SELECT level FROM players WHERE id=$1{(update?" FOR UPDATE":"")}",c,t);q.Parameters.AddWithValue(player);var v=await q.ExecuteScalarAsync(ct);if(v is null)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);return Convert.ToInt32(v);}
    static async Task<(int level,int official)> TargetInfoAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,int general,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT g.level,p.official_id FROM players p JOIN player_generals g ON g.player_id=p.id AND g.general_id=$2 WHERE p.id=$1",c,t);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(general);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PRISON_GENERAL_MISSING","Võ tướng tù nhân không tồn tại.",404);return(r.GetInt32(0),r.GetInt32(1));}

    static async Task SpendGoldAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,int amount,CancellationToken ct)
    {
        int user,sys;await using(var q=new NpgsqlCommand("SELECT user_gold,sys_gold FROM players WHERE id=$1 FOR UPDATE",c,t)){q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);user=r.GetInt32(0);sys=r.GetInt32(1);}
        if((long)user+sys<amount)throw new GameException("GOLD_NOT_ENOUGH","Hoàng Kim không đủ.");var useUser=Math.Min(user,amount);var useSys=amount-useUser;
        await using var pay=new NpgsqlCommand("UPDATE players SET user_gold=user_gold-$2,sys_gold=sys_gold-$3,updated_at=now() WHERE id=$1",c,t);pay.Parameters.AddWithValue(player);pay.Parameters.AddWithValue(useUser);pay.Parameters.AddWithValue(useSys);await pay.ExecuteNonQueryAsync(ct);
    }

    async Task RequireOpenAsync(long player,CancellationToken ct){await using var c=await db.DataSource.OpenConnectionAsync(ct);await RequireOpenAsync(c,null,player,ct);}
    static async Task RequireOpenAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2",c,t);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(FunctionId);if(await q.ExecuteScalarAsync(ct)is null)throw new GameException("PRISON_LOCKED","Hệ thống Lao Phòng chưa mở.",403);}
    static async Task<Holder?> ReadHolderAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,bool update,CancellationToken ct){await using var q=new NpgsqlCommand($"SELECT prison_lv,lash_lv,grab_num,lash_num,auto_lash_exp,point,expire_at,trail_gold FROM player_prisons WHERE player_id=$1{(update?" FOR UPDATE":"")}",c,t);q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;return new(r.GetInt16(0),r.GetInt16(1),r.GetInt32(2),r.GetInt32(3),r.GetInt64(4),r.GetInt32(5),r.IsDBNull(6)?null:r.GetFieldValue<DateTimeOffset>(6),r.GetInt32(7));}
}
