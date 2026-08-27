using System.Text.Json;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfzbCoreRewardView(
    long SeasonId,
    int[] RewardInfo,
    int DoneNum,
    long TotalTickets,
    long PendingTickets,
    string? Title,
    int? EliminatedLayer,
    bool Eliminated,
    bool EventEnded);

public sealed record KfzbCoreRewardClaimResult(
    long SeasonId,
    long TicketsGranted,
    int DoneNum,
    int RewardCount,
    long TicketBalance);

public static class KfzbRewardLifecycle
{
    const string ChampionTitle = "天下第一擂主";
    const string RunnerUpTitle = "天下第二擂主";
    const string Top4Title = "四强擂主";
    const string Top8Title = "八强擂主";
    const string QualifierTitle = "海选擂主";

    sealed record RewardRow(long SeasonId,long PlayerId,int[] Rewards,int DoneNum,string? Title,int? EliminatedLayer,bool Eliminated,int GlobalState);
    sealed record AutoMail(long SeasonId,long PlayerId,string Kind,long Granted,long Total,string? Title,int? EliminatedLayer);

    public static async Task<KfzbCoreRewardView> GetAsync(GameDb db,long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"
SELECT r.season_id,r.reward_info::text,r.done_num,r.title,r.eliminated_layer,
       COALESCE(g.eliminated,false),s.global_state
FROM kfzb_rewards r
JOIN kfzb_seasons s ON s.id=r.season_id
LEFT JOIN kfzb_signups g ON g.season_id=r.season_id AND g.player_id=r.player_id
WHERE r.player_id=$1
ORDER BY s.season_no DESC
LIMIT 1",c);
        q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new GameException("KFZB_REWARD_MISSING","No KFZB reward state exists for this player.",404);
        var rewards=ParseRewards(r.GetString(1));
        var done=ValidateDoneNum(r.GetInt32(2),rewards.Length);
        return new(
            r.GetInt64(0),rewards,done,Sum(rewards,0),Sum(rewards,done),
            r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetInt32(4),r.GetBoolean(5),r.GetInt16(6)>=70);
    }

    public static async Task<KfzbCoreRewardClaimResult> ClaimAsync(GameDb db,GamePushHub push,long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var row=await ReadLatestForUpdateAsync(c,t,playerId,ct);
        var count=row.Rewards.Length;
        var done=ValidateDoneNum(row.DoneNum,count);
        var amount=Sum(row.Rewards,done);
        if(amount<=0)throw new GameException("KFZB_NO_TICKETS","No KFZB ticket reward is pending.",409);

        await GrantPendingAsync(c,t,row.SeasonId,row.PlayerId,row.Rewards,done,false,ct);
        long balance;
        await using(var balanceCmd=new NpgsqlCommand("SELECT tickets FROM player_tickets WHERE player_id=$1",c,t))
        {
            balanceCmd.Parameters.AddWithValue(playerId);
            balance=Convert.ToInt64(await balanceCmd.ExecuteScalarAsync(ct)??0L);
        }
        await t.CommitAsync(ct);
        var result=new KfzbCoreRewardClaimResult(row.SeasonId,amount,count,count,balance);
        await push.SendAsync(playerId,"kfzb.reward",new{reason="claimed",seasonId=row.SeasonId,tickets=amount,doneNum=count,rewardCount=count,balance},ct);
        return result;
    }

    public static async Task MaintainAsync(GameDb db,GamePushHub push,ISystemMailSender mail,CancellationToken ct)
    {
        var autoMails=new List<AutoMail>();
        var titleMails=new List<AutoMail>();
        var endMails=new List<AutoMail>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            await ReconcileTitlesAsync(c,t,ct);

            var rows=new List<RewardRow>();
            await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.player_id,r.reward_info::text,r.done_num,r.title,r.eliminated_layer,
       COALESCE(g.eliminated,false),s.global_state
FROM kfzb_rewards r
JOIN kfzb_seasons s ON s.id=r.season_id
LEFT JOIN kfzb_signups g ON g.season_id=r.season_id AND g.player_id=r.player_id
WHERE (COALESCE(g.eliminated,false) OR s.global_state>=70)
  AND r.done_num < jsonb_array_length(r.reward_info)
ORDER BY r.season_id,r.player_id
FOR UPDATE OF r SKIP LOCKED",c,t))
            await using(var r=await q.ExecuteReaderAsync(ct))
                while(await r.ReadAsync(ct))rows.Add(ReadRow(r));

            foreach(var row in rows)
            {
                var done=ValidateDoneNum(row.DoneNum,row.Rewards.Length);
                var amount=Sum(row.Rewards,done);
                if(amount<=0)continue;
                var automatic=true;
                if(await GrantPendingAsync(c,t,row.SeasonId,row.PlayerId,row.Rewards,done,automatic,ct))
                    autoMails.Add(new(row.SeasonId,row.PlayerId,row.Eliminated&&row.GlobalState<70?"eliminated":"end",amount,Sum(row.Rewards,0),row.Title,row.EliminatedLayer));
            }

            var titleRows=new List<RewardRow>();
            await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.player_id,r.reward_info::text,r.done_num,r.title,r.eliminated_layer,
       COALESCE(g.eliminated,false),s.global_state
FROM kfzb_rewards r
JOIN kfzb_seasons s ON s.id=r.season_id
LEFT JOIN kfzb_signups g ON g.season_id=r.season_id AND g.player_id=r.player_id
WHERE s.global_state>=70 AND r.title IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM kfzb_reward_notice_ledger n WHERE n.season_id=r.season_id AND n.player_id=r.player_id AND n.kind='title')
ORDER BY r.season_id,r.player_id
FOR UPDATE OF r SKIP LOCKED",c,t))
            await using(var r=await q.ExecuteReaderAsync(ct))
                while(await r.ReadAsync(ct))titleRows.Add(ReadRow(r));
            foreach(var row in titleRows)titleMails.Add(new(row.SeasonId,row.PlayerId,"title",0,Sum(row.Rewards,0),row.Title,row.EliminatedLayer));

            var finalRows=new List<RewardRow>();
            await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.player_id,r.reward_info::text,r.done_num,r.title,r.eliminated_layer,
       COALESCE(g.eliminated,false),s.global_state
FROM kfzb_rewards r
JOIN kfzb_seasons s ON s.id=r.season_id
LEFT JOIN kfzb_signups g ON g.season_id=r.season_id AND g.player_id=r.player_id
WHERE s.global_state>=70
  AND NOT EXISTS(SELECT 1 FROM kfzb_reward_notice_ledger n WHERE n.season_id=r.season_id AND n.player_id=r.player_id AND n.kind='end')
ORDER BY r.season_id,r.player_id
FOR UPDATE OF r SKIP LOCKED",c,t))
            await using(var r=await q.ExecuteReaderAsync(ct))
                while(await r.ReadAsync(ct))finalRows.Add(ReadRow(r));
            foreach(var row in finalRows)endMails.Add(new(row.SeasonId,row.PlayerId,"end",0,Sum(row.Rewards,0),row.Title,row.EliminatedLayer));

            await t.CommitAsync(ct);
        }

        foreach(var x in autoMails)
        {
            if(x.Kind=="eliminated")
            {
                await mail.SendAsync(x.PlayerId,"争霸赛",$"您的争霸赛已被淘汰，系统自动帮您领取参赛所得奖励：点券{x.Granted}",[],AutoMailKey(x.SeasonId,x.PlayerId,"eliminated"),ct);
                await MarkNoticeAsync(db,x.SeasonId,x.PlayerId,"eliminated",ct);
            }
            await push.SendAsync(x.PlayerId,"kfzb.reward",new{reason=x.Kind=="eliminated"?"autoEliminated":"autoEnd",seasonId=x.SeasonId,tickets=x.Granted},ct);
        }
        foreach(var x in titleMails)
        {
            var pos=PositionName(x.EliminatedLayer);
            await mail.SendAsync(x.PlayerId,"争霸赛",$"恭喜您在本次争霸赛中，以杰出的战斗表现取得了{pos}，获得称号\"{x.Title}\"！",[],AutoMailKey(x.SeasonId,x.PlayerId,"title"),ct);
            await MarkNoticeAsync(db,x.SeasonId,x.PlayerId,"title",ct);
        }
        foreach(var x in endMails)
        {
            await mail.SendAsync(x.PlayerId,"擂台争霸赛结束",$"持续3天的争霸赛已经结束，本届争霸赛获得奖励：点券{x.Total}",[],AutoMailKey(x.SeasonId,x.PlayerId,"end"),ct);
            await MarkNoticeAsync(db,x.SeasonId,x.PlayerId,"end",ct);
        }
    }

    static async Task ReconcileTitlesAsync(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
UPDATE kfzb_rewards r
SET title=CASE r.eliminated_layer
    WHEN 1 THEN '天下第二擂主'
    WHEN 2 THEN '四强擂主'
    WHEN 3 THEN '八强擂主'
    WHEN 4 THEN '海选擂主'
    WHEN 0 THEN CASE WHEN s.global_state>=70 THEN '天下第一擂主' ELSE NULL END
    ELSE r.title END,
    updated_at=now()
FROM kfzb_seasons s
WHERE s.id=r.season_id
  AND r.eliminated_layer BETWEEN 0 AND 4
  AND r.title IS DISTINCT FROM CASE r.eliminated_layer
    WHEN 1 THEN '天下第二擂主'
    WHEN 2 THEN '四强擂主'
    WHEN 3 THEN '八强擂主'
    WHEN 4 THEN '海选擂主'
    WHEN 0 THEN CASE WHEN s.global_state>=70 THEN '天下第一擂主' ELSE NULL END
    ELSE r.title END",c,t);
        await q.ExecuteNonQueryAsync(ct);
    }

    static async Task<RewardRow> ReadLatestForUpdateAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT r.season_id,r.player_id,r.reward_info::text,r.done_num,r.title,r.eliminated_layer,
       COALESCE(g.eliminated,false),s.global_state
FROM kfzb_rewards r
JOIN kfzb_seasons s ON s.id=r.season_id
LEFT JOIN kfzb_signups g ON g.season_id=r.season_id AND g.player_id=r.player_id
WHERE r.player_id=$1
ORDER BY s.season_no DESC
LIMIT 1
FOR UPDATE OF r",c,t);
        q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new GameException("KFZB_REWARD_MISSING","No KFZB reward state exists for this player.",404);
        return ReadRow(r);
    }

    static RewardRow ReadRow(NpgsqlDataReader r)=>new(
        r.GetInt64(0),r.GetInt64(1),ParseRewards(r.GetString(2)),r.GetInt32(3),
        r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetInt32(5),r.GetBoolean(6),r.GetInt16(7));

    static async Task<bool> GrantPendingAsync(NpgsqlConnection c,NpgsqlTransaction t,long seasonId,long playerId,int[] rewards,int doneNum,bool automatic,CancellationToken ct)
    {
        var count=rewards.Length;
        doneNum=ValidateDoneNum(doneNum,count);
        var amount=Sum(rewards,doneNum);
        if(amount<=0)return false;
        var key=$"kfzb-core:{seasonId}:{playerId}:{doneNum}:{count}";
        await using(var ledger=new NpgsqlCommand("INSERT INTO player_ticket_grants(grant_key,player_id,amount,source) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING",c,t))
        {
            ledger.Parameters.AddWithValue(key);ledger.Parameters.AddWithValue(playerId);ledger.Parameters.AddWithValue(checked((int)amount));ledger.Parameters.AddWithValue(automatic?"kfzb-core-auto":"kfzb-core-claim");
            if(await ledger.ExecuteNonQueryAsync(ct)==1)await TicketsMarketService.GrantAsync(c,t,playerId,amount,ct);
        }
        await using(var done=new NpgsqlCommand("UPDATE kfzb_rewards SET done_num=$3,updated_at=now() WHERE season_id=$1 AND player_id=$2 AND done_num<$3",c,t))
        {done.Parameters.AddWithValue(seasonId);done.Parameters.AddWithValue(playerId);done.Parameters.AddWithValue(count);await done.ExecuteNonQueryAsync(ct);}
        return true;
    }

    static int[] ParseRewards(string json)
    {
        int[] values;
        try{values=JsonSerializer.Deserialize<int[]>(json)??[];}
        catch(JsonException e){throw new GameException("KFZB_REWARD_DATA_INVALID",$"KFZB reward_info is not a legacy integer list: {e.Message}",500);}
        if(values.Any(x=>x<0))throw new GameException("KFZB_REWARD_DATA_INVALID","KFZB reward_info contains a negative ticket amount.",500);
        return values;
    }

    static int ValidateDoneNum(int doneNum,int count)
    {
        if(doneNum<0||doneNum>count)throw new GameException("KFZB_REWARD_DATA_INVALID",$"KFZB done_num {doneNum} is outside reward_info length {count}.",500);
        return doneNum;
    }

    static long Sum(int[] rewards,int start)
    {
        long total=0;for(var i=start;i<rewards.Length;i++)total=checked(total+rewards[i]);return total;
    }

    static string PositionName(int? layer)=>layer switch{0=>"冠军",1=>"亚军",2=>"4强",3=>"8强",4=>"16强",_=>""};
    static string AutoMailKey(long seasonId,long playerId,string kind)=>$"kfzb-core:{kind}:{seasonId}:{playerId}";

    static async Task MarkNoticeAsync(GameDb db,long seasonId,long playerId,string kind,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("INSERT INTO kfzb_reward_notice_ledger(season_id,player_id,kind) VALUES($1,$2,$3) ON CONFLICT DO NOTHING",c);
        q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(kind);await q.ExecuteNonQueryAsync(ct);
    }
}