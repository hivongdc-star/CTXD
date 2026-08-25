using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class ExperienceService(CanonicalContent content)
{
    const int ChiefUpgradeExpSeries=31; // C[Chief.UpgradeExpS] = 31 in legacy static data.
    public async Task AddAsync(NpgsqlConnection conn,NpgsqlTransaction tx,long playerId,int amount,CancellationToken ct)
    {
        await using var read=new NpgsqlCommand("SELECT level,exp FROM players WHERE id=$1 FOR UPDATE",conn,tx); read.Parameters.AddWithValue(playerId);
        int level; long exp; await using(var r=await read.ExecuteReaderAsync(ct)){ await r.ReadAsync(ct); level=r.GetInt32(0); exp=r.GetInt64(1); }
        exp+=Math.Max(0,amount);
        while(level<200) {
            int need; try { need=content.Serial(ChiefUpgradeExpSeries,level); } catch { break; }
            if(exp<need) break; exp-=need; level++;
        }
        await using var upd=new NpgsqlCommand("UPDATE players SET level=$2,exp=$3,updated_at=now() WHERE id=$1",conn,tx);
        upd.Parameters.AddWithValue(playerId); upd.Parameters.AddWithValue(level); upd.Parameters.AddWithValue(exp); await upd.ExecuteNonQueryAsync(ct);
    }
}
