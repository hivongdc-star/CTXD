using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class NationService(GameDb db,CanonicalContent content)
{
    public async Task<NationView> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);int force,official;DateOnly? claimed;
        await using(var cmd=new NpgsqlCommand("SELECT force_id,official_id,salary_claimed_on FROM players WHERE id=$1",c)){cmd.Parameters.AddWithValue(playerId);await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);force=r.GetInt16(0);official=r.GetInt32(1);claimed=r.IsDBNull(2)?null:r.GetFieldValue<DateOnly>(2);}
        if(force is <1 or >3)throw new GameException("NATION_NOT_CHOSEN","Choose a nation first.");var nations=new List<NationForceView>();await using(var cmd=new NpgsqlCommand("SELECT force_id,level,exp FROM nation_forces ORDER BY force_id",c)){await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var lv=r.GetInt32(1);nations.Add(new(r.GetInt16(0),lv,r.GetInt64(2),content.KingdomLevels.TryGetValue(lv,out var k)?k.UpgradeExp:0));}}
        var own=nations.Single(x=>x.ForceId==force);content.Officials.TryGetValue(official,out var rank);var available=claimed!=DateOnly.FromDateTime(DateTime.UtcNow);return new(force,own.Level,own.Exp,own.MaxExp,official,rank?.ShortName??rank?.Name??"",available,rank?.Output??0,nations);
    }

    public async Task<NationSalaryResponse> ClaimSalaryAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);int official;DateOnly? claimed;await using(var cmd=new NpgsqlCommand("SELECT official_id,salary_claimed_on FROM players WHERE id=$1 FOR UPDATE",c,t)){cmd.Parameters.AddWithValue(playerId);await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);official=r.GetInt32(0);claimed=r.IsDBNull(1)?null:r.GetFieldValue<DateOnly>(1);}if(claimed==DateOnly.FromDateTime(DateTime.UtcNow))throw new GameException("NATION_SALARY_CLAIMED","Salary was already claimed today.");if(!content.Officials.TryGetValue(official,out var rank))throw new GameException("NATION_OFFICIAL_INVALID","Official rank data is missing.",500);await using(var cmd=new NpgsqlCommand("UPDATE player_resources SET copper=copper+$2 WHERE player_id=$1;UPDATE players SET salary_claimed_on=(now() AT TIME ZONE 'utc')::date,updated_at=now() WHERE id=$1",c,t)){cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(rank.Output);await cmd.ExecuteNonQueryAsync(ct);}await t.CommitAsync(ct);return new(rank.Output,false);
    }
}
