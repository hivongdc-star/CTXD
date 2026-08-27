using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record SlaveActivityRewardView(int Position,string Kind,int Value,int State);
public sealed record SlaveActivityView(long ActivityId,DateTimeOffset EndsAt,int BonusExp,int RemainingCaptures,SlaveActivityRewardView[] Rewards);
public sealed record SlaveActivityActionResult(int Position,string Kind,int Value,int BonusExp);

public sealed class SlaveActivityService(GameDb db,ExperienceService experience,GamePushHub push)
{
    const int ActivityType=9;
    const int BonusPerLash=2500;
    static readonly (string kind,int value)[] Rewards=[("playerExp",500000),("playerExp",1000000),("iron",100000),("iron",200000)];

    public async Task<SlaveActivityView> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var active=await ActiveAsync(c,t,ct)??throw new GameException("SLAVE_ACTIVITY_UNAVAILABLE","Slave activity is unavailable.",404);
        var bits=await EnsureAsync(c,t,active.id,playerId,ct);await t.CommitAsync(ct);
        return View(active.id,active.end,bits.unlocked,bits.captured,bits.lashed);
    }

    public async Task<SlaveActivityActionResult> CaptureAsync(long playerId,int position,CancellationToken ct)
    {
        ValidatePosition(position);await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var active=await ActiveAsync(c,t,ct)??throw new GameException("SLAVE_ACTIVITY_UNAVAILABLE","Slave activity is unavailable.",404);
        var bits=await EnsureAsync(c,t,active.id,playerId,ct);var bit=1<<(position-1);
        if((bits.unlocked&bit)==0||(bits.captured&bit)!=0)throw new GameException("SLAVE_ACTIVITY_CAPTURE_INVALID","This activity slave cannot be captured.",409);
        await using(var q=new NpgsqlCommand("UPDATE player_slave_activity SET captured_bits=captured_bits|$3,updated_at=now() WHERE activity_id=$1 AND player_id=$2",c,t)){q.Parameters.AddWithValue(active.id);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(bit);await q.ExecuteNonQueryAsync(ct);}
        var reward=Rewards[position-1];await ApplyRewardAsync(c,t,playerId,reward.kind,reward.value,ct);await t.CommitAsync(ct);
        var result=new SlaveActivityActionResult(position,reward.kind,reward.value,CountBits(bits.lashed)*BonusPerLash);await push.SendAsync(playerId,"activity.updated",new{kind="slave",reason="captured",result},ct);return result;
    }

    public async Task<SlaveActivityActionResult> LashAsync(long playerId,int position,CancellationToken ct)
    {
        ValidatePosition(position);await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var active=await ActiveAsync(c,t,ct)??throw new GameException("SLAVE_ACTIVITY_UNAVAILABLE","Slave activity is unavailable.",404);
        var bits=await EnsureAsync(c,t,active.id,playerId,ct);var bit=1<<(position-1);
        if((bits.captured&bit)==0||(bits.lashed&bit)!=0)throw new GameException("SLAVE_ACTIVITY_LASH_INVALID","This activity slave cannot be lashed.",409);
        await using(var q=new NpgsqlCommand("UPDATE player_slave_activity SET lashed_bits=lashed_bits|$3,updated_at=now() WHERE activity_id=$1 AND player_id=$2",c,t)){q.Parameters.AddWithValue(active.id);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(bit);await q.ExecuteNonQueryAsync(ct);}
        var bonus=CountBits(bits.lashed|bit)*BonusPerLash;await t.CommitAsync(ct);
        var result=new SlaveActivityActionResult(position,"lashBonus",BonusPerLash,bonus);await push.SendAsync(playerId,"activity.updated",new{kind="slave",reason="lashed",result},ct);return result;
    }

    public async Task<int> BonusAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        var active=await ActiveAsync(c,t,ct);if(active is null)return 0;var bits=await EnsureAsync(c,t,active.Value.id,playerId,ct);return CountBits(bits.lashed)*BonusPerLash;
    }

    public async Task UnlockAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int previousLashLevel,CancellationToken ct)
    {
        if(previousLashLevel is<1 or>4)return;var active=await ActiveAsync(c,t,ct);if(active is null)return;
        await EnsureAsync(c,t,active.Value.id,playerId,ct);var bit=1<<(previousLashLevel-1);
        await using var q=new NpgsqlCommand("UPDATE player_slave_activity SET unlocked_bits=unlocked_bits|$3,updated_at=now() WHERE activity_id=$1 AND player_id=$2",c,t);q.Parameters.AddWithValue(active.Value.id);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(bit);await q.ExecuteNonQueryAsync(ct);
    }

    public async Task FinalizeAsync(long activityId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using(var seed=new NpgsqlCommand(@"INSERT INTO player_slave_activity(activity_id,player_id,unlocked_bits)
SELECT $1,p.player_id,(1 << LEAST(4,GREATEST(0,p.lash_lv::integer-1)))-1 FROM player_prisons p
ON CONFLICT(activity_id,player_id) DO NOTHING",c)){seed.Parameters.AddWithValue(activityId);await seed.ExecuteNonQueryAsync(ct);}
        var players=new List<long>();await using(var q=new NpgsqlCommand("SELECT player_id FROM player_slave_activity WHERE activity_id=$1 AND settled_at IS NULL",c)){q.Parameters.AddWithValue(activityId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))players.Add(r.GetInt64(0));}
        foreach(var player in players)
        {
            await using var t=await c.BeginTransactionAsync(ct);int unlocked,captured;
            await using(var q=new NpgsqlCommand("SELECT unlocked_bits,captured_bits FROM player_slave_activity WHERE activity_id=$1 AND player_id=$2 AND settled_at IS NULL FOR UPDATE",c,t)){q.Parameters.AddWithValue(activityId);q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)){await t.RollbackAsync(ct);continue;}unlocked=r.GetInt32(0);captured=r.GetInt32(1);}
            var pending=unlocked&~captured;var exp=0;var iron=0;for(var i=0;i<4;i++)if((pending&(1<<i))!=0){if(Rewards[i].kind=="playerExp")exp+=Rewards[i].value;else iron+=Rewards[i].value;}
            if(exp>0)await experience.AddAsync(c,t,player,exp,ct);if(iron>0){await using var add=new NpgsqlCommand("UPDATE player_resources SET iron=iron+$2 WHERE player_id=$1",c,t);add.Parameters.AddWithValue(player);add.Parameters.AddWithValue(iron);await add.ExecuteNonQueryAsync(ct);}
            await using(var done=new NpgsqlCommand("UPDATE player_slave_activity SET settled_at=now(),updated_at=now() WHERE activity_id=$1 AND player_id=$2",c,t)){done.Parameters.AddWithValue(activityId);done.Parameters.AddWithValue(player);await done.ExecuteNonQueryAsync(ct);}await t.CommitAsync(ct);
            if(exp>0||iron>0)await push.SendAsync(player,"activity.updated",new{kind="slave",reason="expired",exp,iron},ct);
        }
    }

    static async Task<(long id,DateTimeOffset end)?> ActiveAsync(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT id,end_at FROM scheduled_activities WHERE activity_type=$1 AND status=1 AND start_at<=now() AND end_at>now() ORDER BY start_at DESC LIMIT 1",c,t);q.Parameters.AddWithValue(ActivityType);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?(r.GetInt64(0),r.GetFieldValue<DateTimeOffset>(1)):null;}
    static async Task<(int unlocked,int captured,int lashed)> EnsureAsync(NpgsqlConnection c,NpgsqlTransaction t,long activity,long player,CancellationToken ct)
    {
        int lash;await using(var q=new NpgsqlCommand("SELECT lash_lv FROM player_prisons WHERE player_id=$1",c,t)){q.Parameters.AddWithValue(player);var raw=await q.ExecuteScalarAsync(ct);if(raw is null)throw new GameException("PRISON_MISSING","Chưa xây Lao Phòng.",403);lash=Convert.ToInt32(raw);}
        var initial=(1<<Math.Clamp(lash-1,0,4))-1;await using(var add=new NpgsqlCommand("INSERT INTO player_slave_activity(activity_id,player_id,unlocked_bits) VALUES($1,$2,$3) ON CONFLICT DO NOTHING",c,t)){add.Parameters.AddWithValue(activity);add.Parameters.AddWithValue(player);add.Parameters.AddWithValue(initial);await add.ExecuteNonQueryAsync(ct);}
        await using var read=new NpgsqlCommand("SELECT unlocked_bits,captured_bits,lashed_bits FROM player_slave_activity WHERE activity_id=$1 AND player_id=$2 FOR UPDATE",c,t);read.Parameters.AddWithValue(activity);read.Parameters.AddWithValue(player);await using var r=await read.ExecuteReaderAsync(ct);await r.ReadAsync(ct);return(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2));
    }
    async Task ApplyRewardAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,string kind,int value,CancellationToken ct){if(kind=="playerExp"){await experience.AddAsync(c,t,player,value,ct);return;}await using var q=new NpgsqlCommand("UPDATE player_resources SET iron=iron+$2 WHERE player_id=$1",c,t);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(value);await q.ExecuteNonQueryAsync(ct);}
    static SlaveActivityView View(long id,DateTimeOffset end,int unlocked,int captured,int lashed)=>new(id,end,CountBits(lashed)*BonusPerLash,4-CountBits(captured),Rewards.Select((x,i)=>new SlaveActivityRewardView(i+1,x.kind,x.value,(unlocked&(1<<i))==0?0:(captured&(1<<i))==0?1:(lashed&(1<<i))==0?2:3)).ToArray());
    static int CountBits(int value){var count=0;for(;value!=0;value&=value-1)count++;return count;}
    static void ValidatePosition(int position){if(position is<1 or>4)throw new GameException("SLAVE_ACTIVITY_POSITION_INVALID","Slave activity position must be 1-4.");}
}
