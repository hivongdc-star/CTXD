using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed partial class PrisonService
{
    async Task DiscoverCaptureAttemptsAsync(CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        // Legacy CityBuilder enqueues slaveService.dealSlave only after a true-player general
        // dies. We mirror that asynchronously from committed battle rounds and restrict the
        // scan to standard world-city battle type 3; mine/nation/cross-server modes do not
        // inherit prison capture without their own legacy evidence.
        await using var q=new NpgsqlCommand(@"
WITH deaths AS (
    SELECT br.battle_id,br.round_no,br.defender_unit_id AS killed_unit_id,
           a.player_id AS holder_player_id,d.player_id AS slave_player_id,d.general_id
    FROM battle_rounds br
    JOIN world_battle_handoffs h ON h.id=br.battle_id AND h.battle_type=3
    JOIN battle_units a ON a.id=br.attacker_unit_id
    JOIN battle_units d ON d.id=br.defender_unit_id
    WHERE br.defender_hp<=0 AND a.player_id IS NOT NULL AND d.player_id IS NOT NULL
      AND a.player_id<>d.player_id AND NOT a.is_phantom AND NOT d.is_phantom
    UNION ALL
    SELECT br.battle_id,br.round_no,br.attacker_unit_id,
           d.player_id,a.player_id,a.general_id
    FROM battle_rounds br
    JOIN world_battle_handoffs h ON h.id=br.battle_id AND h.battle_type=3
    JOIN battle_units a ON a.id=br.attacker_unit_id
    JOIN battle_units d ON d.id=br.defender_unit_id
    WHERE br.attacker_hp<=0 AND a.player_id IS NOT NULL AND d.player_id IS NOT NULL
      AND a.player_id<>d.player_id AND NOT a.is_phantom AND NOT d.is_phantom
), pending AS (
    SELECT x.*,
           (SELECT count(*)::integer FROM battle_rounds prior
            WHERE prior.battle_id=x.battle_id AND prior.round_no<=x.round_no
              AND ((prior.attacker_unit_id=x.killed_unit_id AND prior.defender_hp<=0)
                OR (prior.defender_unit_id=x.killed_unit_id AND prior.attacker_hp<=0))) AS kill_general
    FROM deaths x
    LEFT JOIN prison_capture_attempts p ON p.battle_id=x.battle_id AND p.killed_unit_id=x.killed_unit_id
    WHERE p.battle_id IS NULL
    ORDER BY x.battle_id,x.round_no
    LIMIT 100
)
INSERT INTO prison_capture_attempts(battle_id,killed_unit_id,holder_player_id,slave_player_id,general_id,kill_general)
SELECT battle_id,killed_unit_id,holder_player_id,slave_player_id,general_id,kill_general FROM pending
ON CONFLICT(battle_id,killed_unit_id) DO NOTHING",c,t);
        await q.ExecuteNonQueryAsync(ct);
        await t.CommitAsync(ct);
    }

    public async Task TickDueAsync(CancellationToken ct)
    {
        await DiscoverCaptureAttemptsAsync(ct);
        var captures=new List<(long battle,long unit)>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT battle_id,killed_unit_id FROM prison_capture_attempts WHERE processed_at IS NULL ORDER BY created_at LIMIT 100",c))
        await using(var r=await q.ExecuteReaderAsync(ct)){while(await r.ReadAsync(ct))captures.Add((r.GetInt64(0),r.GetInt64(1)));}
        foreach(var capture in captures)await ProcessCaptureAsync(capture.battle,capture.unit,ct);

        var due=new List<long>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand(@"SELECT id FROM player_slaves WHERE type=1 AND ((escape_at IS NOT NULL AND escape_at<=now()) OR (slash_times>0 AND grab_time+interval '3 days'<=now())) ORDER BY id LIMIT 100",c))
        await using(var r=await q.ExecuteReaderAsync(ct)){while(await r.ReadAsync(ct))due.Add(r.GetInt64(0));}
        foreach(var id in due)await ReleaseDueAsync(id,ct);
    }

    async Task TickPlayerAsync(long playerId,CancellationToken ct)
    {
        var due=new List<long>();await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using(var q=new NpgsqlCommand(@"SELECT id FROM player_slaves WHERE type=1 AND (holder_player_id=$1 OR slave_player_id=$1) AND ((escape_at IS NOT NULL AND escape_at<=now()) OR (slash_times>0 AND grab_time+interval '3 days'<=now()))",c)){q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))due.Add(r.GetInt64(0));}
        foreach(var id in due)await ReleaseDueAsync(id,ct);
    }

    async Task<PrisonCaptureResult> ProcessCaptureAsync(long battleId,long killedUnitId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        long holderPlayer,slavePlayer;int general,killGeneral;bool? oldCaptured;double oldProbability;
        await using(var q=new NpgsqlCommand("SELECT holder_player_id,slave_player_id,general_id,kill_general,captured,probability FROM prison_capture_attempts WHERE battle_id=$1 AND killed_unit_id=$2 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(battleId);q.Parameters.AddWithValue(killedUnitId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)){await t.CommitAsync(ct);return new(false,0,null);}holderPlayer=r.GetInt64(0);slavePlayer=r.GetInt64(1);general=r.GetInt32(2);killGeneral=r.GetInt32(3);oldCaptured=r.IsDBNull(4)?null:r.GetBoolean(4);oldProbability=r.GetDouble(5);}
        if(oldCaptured.HasValue){await t.CommitAsync(ct);return new(oldCaptured.Value,oldProbability,null);}
        var holder=await ReadHolderAsync(c,t,holderPlayer,true,ct);
        if(holder is null){await MarkCaptureAsync(c,t,battleId,killedUnitId,0,false,ct);await t.CommitAsync(ct);return new(false,0,null);}
        await using(var duplicate=new NpgsqlCommand("SELECT id FROM player_slaves WHERE slave_player_id=$1 AND general_id=$2 AND type=1",c,t)){duplicate.Parameters.AddWithValue(slavePlayer);duplicate.Parameters.AddWithValue(general);if(await duplicate.ExecuteScalarAsync(ct)is not null){await MarkCaptureAsync(c,t,battleId,killedUnitId,0,false,ct);await t.CommitAsync(ct);return new(false,0,null);}}
        var row=CatchRow(holder.GrabNum,holder.PrisonLv);var rate=Math.Max(.3,1-(killGeneral-1)*.1);var probability=(row?.Prob??0)*rate;
        var captured=row is not null&&Random.Shared.NextDouble()<probability;
        if(!captured){await MarkCaptureAsync(c,t,battleId,killedUnitId,probability,false,ct);await t.CommitAsync(ct);return new(false,probability,null);}
        int level,force,official;string playerName;
        await using(var target=new NpgsqlCommand("SELECT g.level,p.force_id,p.official_id,COALESCE(p.display_name,'') FROM players p JOIN player_generals g ON g.player_id=p.id AND g.general_id=$2 WHERE p.id=$1 FOR UPDATE OF g,p",c,t))
        {target.Parameters.AddWithValue(slavePlayer);target.Parameters.AddWithValue(general);await using var r=await target.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)){await MarkCaptureAsync(c,t,battleId,killedUnitId,probability,false,ct);await t.CommitAsync(ct);return new(false,probability,null);}level=r.GetInt32(0);force=r.GetInt16(1);official=r.GetInt32(2);playerName=r.GetString(3);}
        var slash=holder.PrisonLv>=5?1:0;long slaveId;
        await using(var add=new NpgsqlCommand(@"INSERT INTO player_slaves(holder_player_id,slave_player_id,general_id,slash_times,type,force_id,name,level) VALUES($1,$2,$3,$4,1,$5,$6,$7) RETURNING id",c,t))
        {add.Parameters.AddWithValue(holderPlayer);add.Parameters.AddWithValue(slavePlayer);add.Parameters.AddWithValue(general);add.Parameters.AddWithValue(slash);add.Parameters.AddWithValue(force);add.Parameters.AddWithValue(playerName);add.Parameters.AddWithValue(level);slaveId=Convert.ToInt64(await add.ExecuteScalarAsync(ct));}
        await using(var grab=new NpgsqlCommand("UPDATE player_prisons SET grab_num=grab_num+1,updated_at=now() WHERE player_id=$1",c,t)){grab.Parameters.AddWithValue(holderPlayer);await grab.ExecuteNonQueryAsync(ct);}
        await using(var state=new NpgsqlCommand("UPDATE player_generals SET state=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t)){state.Parameters.AddWithValue(slavePlayer);state.Parameters.AddWithValue(general);state.Parameters.AddWithValue(CapturedState);await state.ExecuteNonQueryAsync(ct);}
        if(slash>0)
        {
            var degree=data.Degrees[EffectiveLashLevel(holder)];var reward=RewardExp(holder.PrisonLv,level,official)+degree.ExpExtra+await slaveActivity.BonusAsync(c,t,holderPlayer,ct);
            await experience.AddAsync(c,t,holderPlayer,reward,ct);
            await using(var exp=new NpgsqlCommand("UPDATE player_prisons SET auto_lash_exp=auto_lash_exp+$2,updated_at=now() WHERE player_id=$1",c,t)){exp.Parameters.AddWithValue(holderPlayer);exp.Parameters.AddWithValue(reward);await exp.ExecuteNonQueryAsync(ct);}
            await TryAddPointAsync(c,t,holderPlayer,holder,ct);
        }
        await MarkCaptureAsync(c,t,battleId,killedUnitId,probability,true,ct);await t.CommitAsync(ct);
        await push.SendAsync(holderPlayer,"prison.updated",new{reason="captured",slaveId,slavePlayerId=slavePlayer,generalId=general},ct);
        await push.SendAsync(slavePlayer,"prison.updated",new{reason="captured",slaveId,holderPlayerId=holderPlayer,generalId=general},ct);
        return new(true,probability,slaveId);
    }

    async Task ReleaseDueAsync(long slaveId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        long holder,slave;int general,slash;DateTimeOffset grab;DateTimeOffset? escape;
        await using(var q=new NpgsqlCommand("SELECT holder_player_id,slave_player_id,general_id,slash_times,grab_time,escape_at FROM player_slaves WHERE id=$1 AND type=1 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(slaveId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)){await t.CommitAsync(ct);return;}holder=r.GetInt64(0);slave=r.GetInt64(1);general=r.GetInt32(2);slash=r.GetInt32(3);grab=r.GetFieldValue<DateTimeOffset>(4);escape=r.IsDBNull(5)?null:r.GetFieldValue<DateTimeOffset>(5);}
        var now=DateTimeOffset.UtcNow;var due=(escape.HasValue&&escape.Value<=now)||(slash>0&&grab.AddDays(3)<=now);if(!due){await t.CommitAsync(ct);return;}
        await DeleteSlaveAsync(c,t,slaveId,slave,general,ct);await t.CommitAsync(ct);
        await push.SendAsync(holder,"prison.updated",new{reason="escaped",slaveId,generalId=general},ct);
        await push.SendAsync(slave,"prison.updated",new{reason="escaped",slaveId,generalId=general},ct);
    }
}
