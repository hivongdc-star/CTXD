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
    bool EventEnded,
    GeneralTreasureView? Treasure);

public sealed record KfzbCoreRewardClaimResult(
    long SeasonId,
    long TicketsGranted,
    int DoneNum,
    int RewardCount,
    long TicketBalance);

public static class KfzbRewardLifecycle
{
    // Authoritative gcld_kf_gw/kfzb_reward_info row pk=1.
    const int Day1BaseTicket=200;
    const int Day1RoundTicketAdd=100;
    public const int SupportTicket=150;
    const int Layer1Ticket=200;
    const int Layer2Ticket=300;
    const int Layer3Ticket=400;
    const int Layer4Ticket=500;
    const int LegacyFirstRoundBonus=5000;

    const string ChampionTitle="天下第一擂主";
    const string RunnerUpTitle="天下第二擂主";
    const string Top4Title="四强擂主";
    const string Top8Title="八强擂主";
    const string QualifierTitle="海选擂主";

    sealed record RewardRow(long SeasonId,long PlayerId,int[] Rewards,int DoneNum,string? Title,int? EliminatedLayer,bool Eliminated,int GlobalState);
    sealed record AutoMail(long SeasonId,long PlayerId,string Kind,long Granted,long Total,string? Title,int? EliminatedLayer);
    sealed record TreasureNotice(long SeasonId,long PlayerId,int? EliminatedLayer,GeneralTreasureView Treasure);
    sealed record MatchProjection(int Layer,int Round,int State,long? WinnerPlayerId);
    sealed record TreasureReward(int Pos,int TreasureId,int Lea,int Str);

    static readonly IReadOnlyDictionary<int,TreasureReward> TreasureRewards=new Dictionary<int,TreasureReward>
    {
        [1]=new(1,6,90,90),
        [2]=new(2,6,60,60),
        [3]=new(3,5,40,40),
        [4]=new(4,5,35,35),
        [5]=new(5,4,30,30)
    };

    public static async Task<KfzbCoreRewardView> GetAsync(GameDb db,long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var row=await ReadLatestForUpdateAsync(c,t,playerId,ct);
        row=await RefreshRewardInfoAsync(c,t,row,ct);
        var treasure=await GeneralTreasureService.FindBySourceKeyAsync(c,t,playerId,TreasureSourceKey(row.SeasonId,playerId),ct);
        await t.CommitAsync(ct);
        var done=ValidateDoneNum(row.DoneNum,row.Rewards.Length);
        return new(row.SeasonId,row.Rewards,done,Sum(row.Rewards,0),Sum(row.Rewards,done),row.Title,row.EliminatedLayer,row.Eliminated,row.GlobalState>=70,treasure);
    }

    public static async Task<KfzbCoreRewardClaimResult> ClaimAsync(GameDb db,GamePushHub push,long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var row=await ReadLatestForUpdateAsync(c,t,playerId,ct);
        row=await RefreshRewardInfoAsync(c,t,row,ct);
        var count=row.Rewards.Length;var done=ValidateDoneNum(row.DoneNum,count);var amount=Sum(row.Rewards,done);
        if(amount<=0)throw new GameException("KFZB_NO_TICKETS","No KFZB ticket reward is pending.",409);
        await GrantPendingAsync(c,t,row.SeasonId,row.PlayerId,row.Rewards,done,false,ct);
        long balance;await using(var balanceCmd=new NpgsqlCommand("SELECT tickets FROM player_tickets WHERE player_id=$1",c,t)){balanceCmd.Parameters.AddWithValue(playerId);balance=Convert.ToInt64(await balanceCmd.ExecuteScalarAsync(ct)??0L);}
        await t.CommitAsync(ct);
        var result=new KfzbCoreRewardClaimResult(row.SeasonId,amount,count,count,balance);
        await push.SendAsync(playerId,"kfzb.reward",new{reason="claimed",seasonId=row.SeasonId,tickets=amount,doneNum=count,rewardCount=count,balance},ct);
        return result;
    }

    public static async Task MaintainAsync(GameDb db,GamePushHub push,ISystemMailSender mail,CancellationToken ct)
    {
        var autoMails=new List<AutoMail>();var titleMails=new List<AutoMail>();var endMails=new List<AutoMail>();var treasureMails=new List<TreasureNotice>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var t=await c.BeginTransactionAsync(ct))
        {
            await RefreshAllRewardInfoAsync(c,t,ct);
            await ReconcileTitlesAsync(c,t,ct);
            await GrantTerminalTreasuresAsync(c,t,treasureMails,ct);

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
            await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))rows.Add(ReadRow(r));

            foreach(var row in rows)
            {
                var done=ValidateDoneNum(row.DoneNum,row.Rewards.Length);var amount=Sum(row.Rewards,done);if(amount<=0)continue;
                if(await GrantPendingAsync(c,t,row.SeasonId,row.PlayerId,row.Rewards,done,true,ct))autoMails.Add(new(row.SeasonId,row.PlayerId,row.Eliminated&&row.GlobalState<70?"eliminated":"end",amount,Sum(row.Rewards,0),row.Title,row.EliminatedLayer));
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
            await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))titleRows.Add(ReadRow(r));
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
            await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))finalRows.Add(ReadRow(r));
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
        foreach(var x in treasureMails)
        {
            var pos=PositionName(x.EliminatedLayer);
            await mail.SendAsync(x.PlayerId,"争霸赛",$"争霸赛{pos}宝物奖励：{x.Treasure.Name}，统率+{x.Treasure.Lea}，武力+{x.Treasure.Str}",[],AutoMailKey(x.SeasonId,x.PlayerId,"treasure"),ct);
            await MarkNoticeAsync(db,x.SeasonId,x.PlayerId,"treasure",ct);
            await push.SendAsync(x.PlayerId,"kfzb.reward",new{reason="treasure",seasonId=x.SeasonId,treasure=x.Treasure},ct);
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

    // Exact legacy KfzbTimeControlService.getTicketByLayerAndFinish(layer,finish,campaign).
    public static int[] BuildRewardInfo(int totalLayer,int layer,bool finish,bool campaign)
    {
        if(totalLayer<0||layer<1||layer>Math.Max(totalLayer,1))throw new GameException("KFZB_REWARD_PROGRESS_INVALID",$"Invalid KFZB reward progression totalLayer={totalLayer}, layer={layer}.",500);
        if(totalLayer==0)return [];
        var result=new List<int>();
        if(layer>4)
        {
            if(layer==totalLayer&&finish)return [LegacyFirstRoundBonus];
            for(var i=0;i<totalLayer-layer;i++)result.Add(i==0?LegacyFirstRoundBonus+Day1BaseTicket+i*Day1RoundTicketAdd:Day1BaseTicket+i*Day1RoundTicketAdd);
            return result.ToArray();
        }
        for(var i=0;i<totalLayer-4;i++)result.Add(i==0?LegacyFirstRoundBonus+Day1BaseTicket+i*Day1RoundTicketAdd:Day1BaseTicket+i*Day1RoundTicketAdd);
        if(layer==4)
        {
            if(finish)result.Add(Layer4Ticket/2);
            return result.ToArray();
        }
        if(layer==3)
        {
            result.Add(Layer4Ticket);if(finish)result.Add(Layer3Ticket/2);return result.ToArray();
        }
        if(layer==2)
        {
            result.Add(Layer4Ticket);result.Add(Layer3Ticket);if(finish)result.Add(Layer2Ticket/2);return result.ToArray();
        }
        result.Add(Layer4Ticket);result.Add(Layer3Ticket);result.Add(Layer2Ticket);
        if(!finish&&!campaign)return result.ToArray();
        result.Add(!finish&&campaign?Layer1Ticket:Layer1Ticket/2);
        return result.ToArray();
    }

    static async Task RefreshAllRewardInfoAsync(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        var keys=new List<(long season,long player)>();
        await using(var q=new NpgsqlCommand("SELECT season_id,player_id FROM kfzb_rewards ORDER BY season_id,player_id FOR UPDATE SKIP LOCKED",c,t))
        await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))keys.Add((r.GetInt64(0),r.GetInt64(1)));
        foreach(var key in keys)
        {
            var row=await ReadOneForUpdateAsync(c,t,key.season,key.player,ct);await RefreshRewardInfoAsync(c,t,row,ct);
        }
    }

    static async Task<RewardRow> RefreshRewardInfoAsync(NpgsqlConnection c,NpgsqlTransaction t,RewardRow row,CancellationToken ct)
    {
        int warriors;await using(var q=new NpgsqlCommand("SELECT count(*)::int FROM kfzb_signups WHERE season_id=$1",c,t)){q.Parameters.AddWithValue(row.SeasonId);warriors=Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
        var totalLayer=CeilLog2(warriors);int[] generated;
        if(totalLayer==0)generated=[];
        else if(row.EliminatedLayer is int eliminated)
        {
            if(eliminated==0)generated=BuildRewardInfo(totalLayer,1,false,row.GlobalState>=70);
            else generated=BuildRewardInfo(totalLayer,eliminated,true,false);
        }
        else
        {
            var match=await CurrentProjectionAsync(c,t,row.SeasonId,row.PlayerId,ct);
            if(match is null)generated=BuildRewardInfo(totalLayer,totalLayer,false,false);
            else
            {
                var layer=match.Layer;
                if(match.State==2&&match.WinnerPlayerId==row.PlayerId&&match.Round==LayerBattleNum(layer)&&layer>1)layer--;
                generated=BuildRewardInfo(totalLayer,layer,false,false);
            }
        }
        if(row.Rewards.SequenceEqual(generated))return row;
        ValidateDoneNum(row.DoneNum,row.Rewards.Length);
        if(row.Rewards.Length>generated.Length)return row; // never regress coordinator/persisted progression
        for(var i=0;i<row.Rewards.Length;i++)if(row.Rewards[i]!=generated[i])throw new GameException("KFZB_REWARD_PROGRESS_CONFLICT",$"Persisted KFZB reward_info diverges from authoritative progression at index {i}.",500);
        var json=JsonSerializer.Serialize(generated);
        await using(var q=new NpgsqlCommand("UPDATE kfzb_rewards SET reward_info=$3::jsonb,updated_at=now() WHERE season_id=$1 AND player_id=$2",c,t)){q.Parameters.AddWithValue(row.SeasonId);q.Parameters.AddWithValue(row.PlayerId);q.Parameters.AddWithValue(json);await q.ExecuteNonQueryAsync(ct);}
        return row with{Rewards=generated};
    }

    static int CeilLog2(int count){if(count<=1)return 0;var layer=0;long size=1;while(size<count){size<<=1;layer++;}return layer;}
    static int LayerBattleNum(int layer)=>layer switch{1=>5,2=>5,3=>3,4=>3,_=>1};

    static async Task<MatchProjection?> CurrentProjectionAsync(NpgsqlConnection c,NpgsqlTransaction t,long seasonId,long playerId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT layer_no,round_no,state,winner_player_id
FROM kfzb_matches
WHERE season_id=$1 AND phase=2 AND (player1_id=$2 OR player2_id=$2)
ORDER BY layer_no ASC,round_no DESC,id DESC
LIMIT 1",c,t);
        q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;return new(r.GetInt32(0),r.GetInt32(1),r.GetInt16(2),r.IsDBNull(3)?null:r.GetInt64(3));
    }

    static async Task GrantTerminalTreasuresAsync(NpgsqlConnection c,NpgsqlTransaction t,List<TreasureNotice> notices,CancellationToken ct)
    {
        var terminal=new List<(long season,long player,int layer,int state)>();
        await using(var q=new NpgsqlCommand(@"
SELECT r.season_id,r.player_id,r.eliminated_layer,s.global_state
FROM kfzb_rewards r JOIN kfzb_seasons s ON s.id=r.season_id
WHERE (r.eliminated_layer BETWEEN 1 AND 4)
   OR (r.eliminated_layer=0 AND s.global_state>=70)
ORDER BY r.season_id,r.player_id
FOR UPDATE OF r SKIP LOCKED",c,t))
        await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))terminal.Add((r.GetInt64(0),r.GetInt64(1),r.GetInt32(2),r.GetInt16(3)));
        foreach(var x in terminal)
        {
            var pos=x.layer==0?1:x.layer+1;if(!TreasureRewards.TryGetValue(pos,out var reward))continue;
            var key=TreasureSourceKey(x.season,x.player);var (view,_)=await GeneralTreasureService.GrantAsync(c,t,x.player,reward.TreasureId,reward.Lea,reward.Str,"kfzb",key,ct);
            await using var notice=new NpgsqlCommand("SELECT 1 FROM kfzb_reward_notice_ledger WHERE season_id=$1 AND player_id=$2 AND kind='treasure'",c,t);notice.Parameters.AddWithValue(x.season);notice.Parameters.AddWithValue(x.player);if(await notice.ExecuteScalarAsync(ct)is null)notices.Add(new(x.season,x.player,x.layer,view));
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
    ELSE NULL END,
    updated_at=now()
FROM kfzb_seasons s
WHERE s.id=r.season_id
  AND r.eliminated_layer IS NOT NULL
  AND r.title IS DISTINCT FROM CASE r.eliminated_layer
    WHEN 1 THEN '天下第二擂主'
    WHEN 2 THEN '四强擂主'
    WHEN 3 THEN '八强擂主'
    WHEN 4 THEN '海选擂主'
    WHEN 0 THEN CASE WHEN s.global_state>=70 THEN '天下第一擂主' ELSE NULL END
    ELSE NULL END",c,t);
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
        q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFZB_REWARD_MISSING","No KFZB reward state exists for this player.",404);return ReadRow(r);
    }

    static async Task<RewardRow> ReadOneForUpdateAsync(NpgsqlConnection c,NpgsqlTransaction t,long seasonId,long playerId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT r.season_id,r.player_id,r.reward_info::text,r.done_num,r.title,r.eliminated_layer,
       COALESCE(g.eliminated,false),s.global_state
FROM kfzb_rewards r
JOIN kfzb_seasons s ON s.id=r.season_id
LEFT JOIN kfzb_signups g ON g.season_id=r.season_id AND g.player_id=r.player_id
WHERE r.season_id=$1 AND r.player_id=$2
FOR UPDATE OF r",c,t);
        q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFZB_REWARD_MISSING","KFZB reward row disappeared during maintenance.",500);return ReadRow(r);
    }

    static RewardRow ReadRow(NpgsqlDataReader r)=>new(r.GetInt64(0),r.GetInt64(1),ParseRewards(r.GetString(2)),r.GetInt32(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetInt32(5),r.GetBoolean(6),r.GetInt16(7));

    static async Task<bool> GrantPendingAsync(NpgsqlConnection c,NpgsqlTransaction t,long seasonId,long playerId,int[] rewards,int doneNum,bool automatic,CancellationToken ct)
    {
        var count=rewards.Length;doneNum=ValidateDoneNum(doneNum,count);var amount=Sum(rewards,doneNum);if(amount<=0)return false;
        var key=$"kfzb-core:{seasonId}:{playerId}:{doneNum}:{count}";
        await using(var ledger=new NpgsqlCommand("INSERT INTO player_ticket_grants(grant_key,player_id,amount,source) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING",c,t))
        {
            ledger.Parameters.AddWithValue(key);ledger.Parameters.AddWithValue(playerId);ledger.Parameters.AddWithValue(checked((int)amount));ledger.Parameters.AddWithValue(automatic?"kfzb-core-auto":"kfzb-core-claim");
            if(await ledger.ExecuteNonQueryAsync(ct)==1)await TicketsMarketService.GrantAsync(c,t,playerId,amount,ct);
        }
        await using(var done=new NpgsqlCommand("UPDATE kfzb_rewards SET done_num=$3,updated_at=now() WHERE season_id=$1 AND player_id=$2 AND done_num<$3",c,t)){done.Parameters.AddWithValue(seasonId);done.Parameters.AddWithValue(playerId);done.Parameters.AddWithValue(count);await done.ExecuteNonQueryAsync(ct);}
        return true;
    }

    static int[] ParseRewards(string json)
    {
        int[] values;try{values=JsonSerializer.Deserialize<int[]>(json)??[];}catch(JsonException e){throw new GameException("KFZB_REWARD_DATA_INVALID",$"KFZB reward_info is not a legacy integer list: {e.Message}",500);}
        if(values.Any(x=>x<0))throw new GameException("KFZB_REWARD_DATA_INVALID","KFZB reward_info contains a negative ticket amount.",500);return values;
    }

    static int ValidateDoneNum(int doneNum,int count){if(doneNum<0||doneNum>count)throw new GameException("KFZB_REWARD_DATA_INVALID",$"KFZB done_num {doneNum} is outside reward_info length {count}.",500);return doneNum;}
    static long Sum(int[] rewards,int start){long total=0;for(var i=start;i<rewards.Length;i++)total=checked(total+rewards[i]);return total;}
    static string PositionName(int? layer)=>layer switch{0=>"冠军",1=>"亚军",2=>"4强",3=>"8强",4=>"16强",_=>""};
    static string AutoMailKey(long seasonId,long playerId,string kind)=>$"kfzb-core:{kind}:{seasonId}:{playerId}";
    static string TreasureSourceKey(long seasonId,long playerId)=>$"kfzb:{seasonId}:{playerId}:treasure";

    static async Task MarkNoticeAsync(GameDb db,long seasonId,long playerId,string kind,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var q=new NpgsqlCommand("INSERT INTO kfzb_reward_notice_ledger(season_id,player_id,kind) VALUES($1,$2,$3) ON CONFLICT DO NOTHING",c);q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(kind);await q.ExecuteNonQueryAsync(ct);
    }
}
