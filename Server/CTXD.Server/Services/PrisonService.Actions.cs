using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed partial class PrisonService
{
    public async Task<PrisonView> GetAsync(long playerId,CancellationToken ct)
    {
        await RequireOpenAsync(playerId,ct);
        await TickPlayerAsync(playerId,ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        var holder=await ReadHolderAsync(c,null,playerId,false,ct);
        var captives=await ReadCaptivesAsync(c,playerId,ct);
        if(holder is null)
        {
            var have=await ItemOwnedAsync(c,null,playerId,data.Levels[1].Drawing,DrawingItemType,ct)>0;
            return new(false,have,0,false,false,0,0,data.Degrees.Count,0,0,0,false,0,null,false,0,5,0,false,0,0,0,Degrees(),[],captives);
        }

        var canUpdate=CanUpdatePrison(holder.PrisonLv,await PlayerLevelAsync(c,null,playerId,false,ct));
        var nextDrawing=holder.PrisonLv<5&&data.Levels.TryGetValue(holder.PrisonLv+1,out var next)?next.Drawing:0;
        var havePic=nextDrawing>0&&await ItemOwnedAsync(c,null,playerId,nextDrawing,DrawingItemType,ct)>0;
        var effectiveLash=EffectiveLashLevel(holder);var degree=data.Degrees[effectiveLash];
        var upgradeGold=holder.LashLv>=data.Degrees.Count?0:Math.Max(0,data.Degrees[holder.LashLv+1].Cost-holder.Point-holder.TrailGold);
        var trialActive=holder.ExpireAt>DateTimeOffset.UtcNow;var trialGold=holder.LashLv>=data.Degrees.Count?0:data.Degrees[holder.LashLv+1].TryGold;
        var canTrial=holder.LashLv<data.Degrees.Count&&!trialActive&&trialGold<upgradeGold;
        var quality=CatchRow(holder.GrabNum,holder.PrisonLv)?.ProbLv??5;
        var haveTech=await technologies.GetCompletedIntEffectAsync(playerId,58,0,ct,c,null)>0;
        var prisoners=await ReadPrisonersAsync(c,playerId,ct);
        return new(true,havePic,holder.PrisonLv,canUpdate,havePic,holder.LashLv,effectiveLash,data.Degrees.Count,degree.ExpExtra,degree.TimeExtra,upgradeGold,trialActive,trialGold,trialActive?holder.ExpireAt:null,canTrial,holder.GrabNum,quality,holder.AutoLashExp,haveTech,holder.Point,data.Degrees[holder.LashLv].ExpFree,data.Degrees[holder.LashLv].ExpSum,Degrees(),prisoners,captives);
    }

    public async Task<PrisonView> BuildAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await RequireOpenAsync(c,t,playerId,ct);
        if(await ReadHolderAsync(c,t,playerId,true,ct)is not null)throw new GameException("PRISON_EXISTS","Lao phòng đã được xây.",409);
        var drawing=data.Levels[1].Drawing;
        if(!await items.ConsumeAsync(c,t,playerId,drawing,DrawingItemType,1,ct))throw new GameException("PRISON_DRAWING_MISSING","Thiếu bản vẽ xây Lao Phòng.");
        await using(var add=new NpgsqlCommand("INSERT INTO player_prisons(player_id,prison_lv,lash_lv) VALUES($1,1,1)",c,t)){add.Parameters.AddWithValue(playerId);await add.ExecuteNonQueryAsync(ct);}
        await QuestService.MarkBuildedLimboAsync(c,t,playerId,ct);
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"prison.updated",new{reason="built",prisonLv=1},ct);
        return await GetAsync(playerId,ct);
    }

    public async Task<PrisonView> UpgradePrisonAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await RequireOpenAsync(c,t,playerId,ct);
        var holder=await ReadHolderAsync(c,t,playerId,true,ct)??throw new GameException("PRISON_MISSING","Chưa xây Lao Phòng.");
        if(holder.PrisonLv>=5)throw new GameException("PRISON_MAX_LEVEL","Lao Phòng đã đạt cấp tối đa.");
        var playerLevel=await PlayerLevelAsync(c,t,playerId,true,ct);
        if(!CanUpdatePrison(holder.PrisonLv,playerLevel))throw new GameException("PRISON_LEVEL_REQUIREMENT","Cấp nhân vật chưa đủ để nâng Lao Phòng.");
        var next=holder.PrisonLv+1;var drawing=data.Levels[next].Drawing;
        if(!await items.ConsumeAsync(c,t,playerId,drawing,DrawingItemType,1,ct))throw new GameException("PRISON_DRAWING_MISSING","Thiếu bản vẽ nâng Lao Phòng.");
        await using(var save=new NpgsqlCommand("UPDATE player_prisons SET prison_lv=$2,updated_at=now() WHERE player_id=$1",c,t)){save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(next);await save.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"prison.updated",new{reason="prison-level",prisonLv=next},ct);
        return await GetAsync(playerId,ct);
    }

    public async Task<PrisonView> UpgradeLashAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await RequireOpenAsync(c,t,playerId,ct);
        var holder=await ReadHolderAsync(c,t,playerId,true,ct)??throw new GameException("PRISON_MISSING","Chưa xây Lao Phòng.");
        if(holder.LashLv>=data.Degrees.Count)throw new GameException("PRISON_LASH_MAX","Cấp roi đã tối đa.");
        var next=data.Degrees[holder.LashLv+1];var gold=Math.Max(0,next.Cost-holder.Point-holder.TrailGold);
        if(gold>0){await SpendGoldAsync(c,t,playerId,gold,ct);await dstq.RecordGoldSpendAsync(c,t,playerId,gold,ct);}
        await using(var save=new NpgsqlCommand("UPDATE player_prisons SET lash_lv=lash_lv+1,expire_at=NULL,trail_gold=0,updated_at=now() WHERE player_id=$1",c,t)){save.Parameters.AddWithValue(playerId);await save.ExecuteNonQueryAsync(ct);}
        await slaveActivity.UnlockAsync(c,t,playerId,holder.LashLv,ct);
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"prison.updated",new{reason="lash-level",lashLv=holder.LashLv+1,gold},ct);
        return await GetAsync(playerId,ct);
    }

    public async Task<PrisonTrialResult> UseTrialAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await RequireOpenAsync(c,t,playerId,ct);
        var holder=await ReadHolderAsync(c,t,playerId,true,ct)??throw new GameException("PRISON_MISSING","Chưa xây Lao Phòng.");
        if(holder.LashLv>=data.Degrees.Count)throw new GameException("PRISON_LASH_MAX","Cấp roi đã tối đa.");
        if(holder.ExpireAt>DateTimeOffset.UtcNow)throw new GameException("PRISON_TRIAL_ACTIVE","Cấp roi dùng thử đang có hiệu lực.",409);
        var next=data.Degrees[holder.LashLv+1];
        await SpendGoldAsync(c,t,playerId,next.TryGold,ct);await dstq.RecordGoldSpendAsync(c,t,playerId,next.TryGold,ct);
        var upgraded=next.TryGold+holder.Point+holder.TrailGold>=next.Cost;DateTimeOffset? ends=null;var lashLv=holder.LashLv;
        if(upgraded)
        {
            lashLv++;
            await using var q=new NpgsqlCommand("UPDATE player_prisons SET lash_lv=$2,expire_at=NULL,trail_gold=0,updated_at=now() WHERE player_id=$1",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(lashLv);await q.ExecuteNonQueryAsync(ct);
            await slaveActivity.UnlockAsync(c,t,playerId,holder.LashLv,ct);
        }
        else
        {
            ends=DateTimeOffset.UtcNow.AddDays(1);
            await using var q=new NpgsqlCommand("UPDATE player_prisons SET expire_at=$2,trail_gold=trail_gold+$3,updated_at=now() WHERE player_id=$1",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(ends.Value);q.Parameters.AddWithValue(next.TryGold);await q.ExecuteNonQueryAsync(ct);
        }
        await t.CommitAsync(ct);var effective=Math.Min(5,lashLv+(ends.HasValue?1:0));
        var result=new PrisonTrialResult(upgraded,lashLv,effective,next.TryGold,ends);await push.SendAsync(playerId,"prison.updated",new{reason="lash-trial",result},ct);return result;
    }

    public async Task<PrisonLashResult> LashAsync(long playerId,long slaveId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await RequireOpenAsync(c,t,playerId,ct);
        var holder=await ReadHolderAsync(c,t,playerId,true,ct)??throw new GameException("PRISON_MISSING","Chưa xây Lao Phòng.");
        long slavePlayer;int general,slashTimes;DateTimeOffset? escapeAt;
        await using(var q=new NpgsqlCommand("SELECT slave_player_id,general_id,slash_times,escape_at FROM player_slaves WHERE id=$1 AND holder_player_id=$2 AND type=1 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(slaveId);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PRISON_SLAVE_MISSING","Tù nhân không tồn tại.",404);slavePlayer=r.GetInt64(0);general=r.GetInt32(1);slashTimes=r.GetInt32(2);escapeAt=r.IsDBNull(3)?null:r.GetFieldValue<DateTimeOffset>(3);}
        if(slashTimes>0)throw new GameException("PRISON_ALREADY_LASHED","Tù nhân đã bị quất roi.",409);
        var (level,official)=await TargetInfoAsync(c,t,slavePlayer,general,ct);
        var effectiveLash=EffectiveLashLevel(holder);var degree=data.Degrees[effectiveLash];var reward=RewardExp(holder.PrisonLv,level,official)+degree.ExpExtra+await slaveActivity.BonusAsync(c,t,playerId,ct);
        await experience.AddAsync(c,t,playerId,reward,ct);
        var added=0;var nextEscape=escapeAt;
        if(escapeAt.HasValue&&effectiveLash>=2){added=degree.TimeExtra;nextEscape=escapeAt.Value.AddSeconds(added);}
        await using(var save=new NpgsqlCommand("UPDATE player_slaves SET slash_times=1,escape_at=$3 WHERE id=$1 AND holder_player_id=$2",c,t)){save.Parameters.AddWithValue(slaveId);save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue((object?)nextEscape??DBNull.Value);await save.ExecuteNonQueryAsync(ct);}
        await using(var exp=new NpgsqlCommand("UPDATE player_prisons SET auto_lash_exp=auto_lash_exp+$2,updated_at=now() WHERE player_id=$1",c,t)){exp.Parameters.AddWithValue(playerId);exp.Parameters.AddWithValue(reward);await exp.ExecuteNonQueryAsync(ct);}
        var point=await TryAddPointAsync(c,t,playerId,holder,ct);
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"prison.updated",new{reason="lash",slaveId,rewardExp=reward,addedEscapeSeconds=added},ct);
        await push.SendAsync(slavePlayer,"prison.updated",new{reason="lashed",generalId=general,escapeAt=nextEscape},ct);
        return new(slaveId,reward,added,effectiveLash,point);
    }

    public async Task<PrisonEscapeResult> EscapeAsync(long playerId,int generalId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        long id,holderPlayer;int slashTimes;
        await using(var q=new NpgsqlCommand("SELECT id,holder_player_id,slash_times FROM player_slaves WHERE slave_player_id=$1 AND general_id=$2 AND type=1 AND escape_at IS NULL FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PRISON_NOT_CAPTIVE","Võ tướng không ở trong Lao Phòng hoặc đang vượt ngục.",404);id=r.GetInt64(0);holderPlayer=r.GetInt64(1);slashTimes=r.GetInt32(2);}
        var extra=0;if(slashTimes>0){var holder=await ReadHolderAsync(c,t,holderPlayer,false,ct);if(holder is not null)extra=data.Degrees[EffectiveLashLevel(holder)].TimeExtra;}
        var seconds=30+extra;var escapeAt=DateTimeOffset.UtcNow.AddSeconds(seconds);
        await using(var save=new NpgsqlCommand("UPDATE player_slaves SET escape_at=$2 WHERE id=$1",c,t)){save.Parameters.AddWithValue(id);save.Parameters.AddWithValue(escapeAt);await save.ExecuteNonQueryAsync(ct);}
        await using(var state=new NpgsqlCommand("UPDATE player_generals SET state=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t)){state.Parameters.AddWithValue(playerId);state.Parameters.AddWithValue(generalId);state.Parameters.AddWithValue(EscapingState);await state.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);
        await push.SendAsync(holderPlayer,"prison.updated",new{reason="escape-started",slaveId=id,generalId,escapeAt},ct);
        await push.SendAsync(playerId,"prison.updated",new{reason="escape-started",slaveId=id,generalId,escapeAt},ct);
        return new(id,generalId,seconds,escapeAt);
    }

    public async Task ReleaseAsync(long playerId,long slaveId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await RequireOpenAsync(c,t,playerId,ct);
        long slavePlayer;int general;
        await using(var q=new NpgsqlCommand("SELECT slave_player_id,general_id FROM player_slaves WHERE id=$1 AND holder_player_id=$2 AND type=1 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(slaveId);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PRISON_SLAVE_MISSING","Tù nhân không tồn tại.",404);slavePlayer=r.GetInt64(0);general=r.GetInt32(1);}
        await SpendGoldAsync(c,t,playerId,FreedomGold,ct);await dstq.RecordGoldSpendAsync(c,t,playerId,FreedomGold,ct);
        await DeleteSlaveAsync(c,t,slaveId,slavePlayer,general,ct);
        await t.CommitAsync(ct);
        await push.SendAsync(playerId,"prison.updated",new{reason="freedom",slaveId,gold=FreedomGold},ct);
        await push.SendAsync(slavePlayer,"prison.updated",new{reason="freedom",generalId=general},ct);
    }
}
