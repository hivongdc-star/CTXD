using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfwdDayRewardView(int Day,int Rank,int Tickets,bool Granted);
public sealed record KfwdGeneralTreasureView(long InstanceId,int TreasureId,string Name,int GoodsType,int Quality,int Leadership,int Strength,bool Overflow);
public sealed record KfwdRewardView(long SeasonId,int GlobalState,KfwdDayRewardView[] Days,bool TreasureClaimed,bool TreasureClaimAvailable,KfwdGeneralTreasureView? Treasure);

public sealed class KfwdRewardService(GameDb db,ISystemMailSender mail,GamePushHub push)
{
    const string RewardMailTitle="擂台赛";
    const string DefaultWdName="先锋擂台赛";

    static readonly (int MaxRank,int Tickets)[][] DayTicketTable =
    [
        [(1,2500),(5,1750),(20,1250),(50,750),(100,500),(200,375),(500,300),(1000,250),(999999,200)],
        [(1,5000),(5,3500),(20,2500),(50,1500),(100,1000),(200,750),(500,600),(1000,500),(999999,400)],
        [(1,10000),(5,7000),(20,5000),(50,3000),(100,2000),(200,1500),(500,1200),(1000,1000),(999999,800)]
    ];

    static readonly TreasureRule[] TreasureTable =
    [
        new(1,1,1,6,100,100,100,100,2,6,"Tử San Hô"),
        new(1,2,30,5,50,80,50,80,2,5,"Dạ Minh Châu"),
        new(1,31,100,4,20,40,20,40,2,4,"Hòa Thị Bích")
    ];

    public async Task TickAsync(CancellationToken ct)
    {
        var seasons=new List<SeasonRewardClock>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT id,global_state,battle_opens_at,round_interval_seconds,total_rounds,one_day_round_limit FROM kfwd_seasons WHERE global_state>=60 AND one_day_round_limit>0 ORDER BY id",c))
        await using(var r=await q.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct))seasons.Add(new(r.GetInt64(0),r.GetInt16(1),r.GetFieldValue<DateTimeOffset>(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5)));

        foreach(var season in seasons)
        {
            var elapsed=Math.Max(0,(DateTimeOffset.UtcNow-season.BattleOpensAt).TotalSeconds);
            var completed=season.GlobalState>=70?season.TotalRounds:Math.Clamp((int)(elapsed/season.RoundIntervalSeconds),0,season.TotalRounds);
            var day1=season.OneDayRoundLimit;
            var day2=season.OneDayRoundLimit*2;
            if(completed>=day1)await FinalizeDayAsync(season.Id,1,day1,ct);
            if(completed>=day2)await FinalizeDayAsync(season.Id,2,day2,ct);
            if(completed>=season.TotalRounds)await FinalizeDayAsync(season.Id,3,season.TotalRounds,ct);
            if(season.GlobalState>=70)
            {
                await AutoCatchupTreasureAsync(season.Id,ct);
                await EnsureAutoTreasureMailsAsync(season.Id,ct);
            }
        }
    }

    public async Task<KfwdRewardView> GetAsync(long playerId,CancellationToken ct)
    {
        await TickAsync(ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        long seasonId;int state;int[] ranks;int mask;bool claimed;
        await using(var q=new NpgsqlCommand(@"SELECT s.id,s.global_state,r.day_ranking,r.day_reward_claimed,r.treasure_claimed
FROM kfwd_signups sg
JOIN kfwd_seasons s ON s.id=sg.season_id
JOIN kfwd_rewards r ON r.season_id=sg.season_id AND r.player_id=sg.player_id
WHERE sg.player_id=$1 ORDER BY s.season_no DESC LIMIT 1",c))
        {
            q.Parameters.AddWithValue(playerId);
            await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("KFWD_NOT_SIGNED","Player has no KFWD reward state.",404);
            seasonId=r.GetInt64(0);state=r.GetInt16(1);ranks=(int[])r.GetValue(2);mask=r.GetInt32(3);claimed=r.GetBoolean(4);
        }
        var days=Enumerable.Range(1,3).Select(day=>new KfwdDayRewardView(day,RankAt(ranks,day),TicketsFor(day,RankAt(ranks,day)),(mask&(1<<(day-1)))!=0)).ToArray();
        var finalRule=FindTreasure(RankAt(ranks,3));
        var treasure=await ReadGrantedTreasureAsync(c,seasonId,playerId,ct);
        return new(seasonId,state,days,claimed,state<70&&!claimed&&finalRule is not null,treasure);
    }

    public async Task<KfwdGeneralTreasureView> ClaimTreasureAsync(long playerId,CancellationToken ct)
    {
        var result=await GrantTreasureAsync(playerId,null,true,ct);
        await push.SendAsync(playerId,"kfwd.updated",new{reason="treasure.claim",result.TreasureId},ct);
        return result;
    }

    async Task FinalizeDayAsync(long seasonId,int day,int boundaryRound,CancellationToken ct)
    {
        if(!await BoundaryReadyAsync(seasonId,boundaryRound,ct))return;
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var rankIndex=day;
        await using(var snapshot=new NpgsqlCommand($@"WITH ranked AS (
 SELECT r.player_id,(row_number() OVER(PARTITION BY sg.schedule_id ORDER BY r.score DESC,r.wins DESC,r.win_res DESC,((sg.competitor_id*97+sg.season_id)%137) DESC,sg.competitor_id DESC))::int AS rank
 FROM kfwd_rewards r JOIN kfwd_signups sg ON sg.season_id=r.season_id AND sg.player_id=r.player_id
 WHERE r.season_id=$1 AND sg.synced=true)
UPDATE kfwd_rewards r SET day_ranking[{rankIndex}]=ranked.rank,updated_at=now()
FROM ranked
WHERE r.season_id=$1 AND r.player_id=ranked.player_id AND COALESCE(r.day_ranking[{rankIndex}],0)=0",c,t))
        {
            snapshot.Parameters.AddWithValue(seasonId);
            await snapshot.ExecuteNonQueryAsync(ct);
        }

        var marked=new List<(long PlayerId,int Rank)>();
        var bit=1<<(day-1);
        await using(var mark=new NpgsqlCommand($"UPDATE kfwd_rewards SET day_reward_claimed=day_reward_claimed|$2,updated_at=now() WHERE season_id=$1 AND (day_reward_claimed&$2)=0 AND day_ranking[{rankIndex}]>0 RETURNING player_id,day_ranking[{rankIndex}]",c,t))
        {
            mark.Parameters.AddWithValue(seasonId);mark.Parameters.AddWithValue(bit);
            await using var r=await mark.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))marked.Add((r.GetInt64(0),r.GetInt32(1)));
        }

        foreach(var x in marked)
        {
            var amount=TicketsFor(day,x.Rank);
            if(amount<=0)continue;
            var key=$"kfwd-day:{seasonId}:{day}:{x.PlayerId}";
            int granted=0;
            await using(var grant=new NpgsqlCommand("INSERT INTO player_ticket_grants(grant_key,player_id,amount,source) VALUES($1,$2,$3,'kfwd-day') ON CONFLICT DO NOTHING RETURNING amount",c,t))
            {
                grant.Parameters.AddWithValue(key);grant.Parameters.AddWithValue(x.PlayerId);grant.Parameters.AddWithValue(amount);
                var value=await grant.ExecuteScalarAsync(ct);if(value is not null)granted=Convert.ToInt32(value);
            }
            if(granted<=0)continue;
            await using(var reward=new NpgsqlCommand("UPDATE kfwd_rewards SET tickets=tickets+$3,updated_at=now() WHERE season_id=$1 AND player_id=$2",c,t))
            {reward.Parameters.AddWithValue(seasonId);reward.Parameters.AddWithValue(x.PlayerId);reward.Parameters.AddWithValue(granted);await reward.ExecuteNonQueryAsync(ct);}
            await using(var wallet=new NpgsqlCommand("INSERT INTO player_tickets(player_id,tickets) VALUES($1,$2) ON CONFLICT(player_id) DO UPDATE SET tickets=player_tickets.tickets+excluded.tickets,updated_at=now()",c,t))
            {wallet.Parameters.AddWithValue(x.PlayerId);wallet.Parameters.AddWithValue(granted);await wallet.ExecuteNonQueryAsync(ct);}
        }
        await t.CommitAsync(ct);
        await EnsureDayMailsAsync(seasonId,day,ct);
    }

    async Task<bool> BoundaryReadyAsync(long seasonId,int boundaryRound,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"SELECT NOT EXISTS(
 SELECT 1 FROM kfwd_signups sg
 WHERE sg.season_id=$1 AND sg.synced=true
 AND NOT EXISTS(SELECT 1 FROM kfwd_matches m WHERE m.season_id=$1 AND m.round_no=$2 AND m.state=2 AND (m.player1_id=sg.player_id OR m.player2_id=sg.player_id)))",c);
        q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(boundaryRound);
        return Convert.ToBoolean(await q.ExecuteScalarAsync(ct));
    }

    async Task EnsureDayMailsAsync(long seasonId,int day,CancellationToken ct)
    {
        var rows=new List<(long PlayerId,int Rank)>();var bit=1<<(day-1);
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand($@"SELECT r.player_id,r.day_ranking[{day}]
FROM kfwd_rewards r
WHERE r.season_id=$1 AND (r.day_reward_claimed&$2)<>0 AND r.day_ranking[{day}]>0
AND NOT EXISTS(SELECT 1 FROM player_mail m WHERE m.recipient_player_id=r.player_id AND m.source_key='kfwd-day:'||r.season_id||':{day}:'||r.player_id)",c))
        {
            q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(bit);
            await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add((r.GetInt64(0),r.GetInt32(1)));
        }
        foreach(var x in rows)
        {
            var amount=TicketsFor(day,x.Rank);if(amount<=0)continue;
            var body=$"您获得领取{DefaultWdName}第{day}天的排名奖励{amount}点券";
            await mail.SendAsync(x.PlayerId,RewardMailTitle,body,Array.Empty<MailAttachment>(), $"kfwd-day:{seasonId}:{day}:{x.PlayerId}",ct);
        }
    }

    async Task AutoCatchupTreasureAsync(long seasonId,CancellationToken ct)
    {
        var players=new List<long>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT player_id FROM kfwd_rewards WHERE season_id=$1 AND NOT treasure_claimed AND day_ranking[3] BETWEEN 1 AND 100 ORDER BY player_id",c))
        {q.Parameters.AddWithValue(seasonId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))players.Add(r.GetInt64(0));}
        foreach(var player in players)await GrantTreasureAsync(player,seasonId,false,ct);
    }

    async Task<KfwdGeneralTreasureView> GrantTreasureAsync(long playerId,long? forcedSeasonId,bool manual,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        long seasonId;int state,rank,maxStore;bool claimed;
        await using(var q=new NpgsqlCommand(@"SELECT s.id,s.global_state,r.day_ranking[3],r.treasure_claimed,p.max_store_num
FROM kfwd_signups sg JOIN kfwd_seasons s ON s.id=sg.season_id JOIN kfwd_rewards r ON r.season_id=sg.season_id AND r.player_id=sg.player_id JOIN players p ON p.id=sg.player_id
WHERE sg.player_id=$1 AND ($2::bigint IS NULL OR s.id=$2) ORDER BY s.season_no DESC LIMIT 1 FOR UPDATE OF r,p",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(forcedSeasonId.HasValue?forcedSeasonId.Value:DBNull.Value);
            await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFWD_NOT_SIGNED","Player has no KFWD reward state.",404);
            seasonId=r.GetInt64(0);state=r.GetInt16(1);rank=r.GetInt32(2);claimed=r.GetBoolean(3);maxStore=r.GetInt32(4);
        }
        if(manual&&state>=70)throw new GameException("KFWD_TREASURE_CLAIM_CLOSED","Legacy KFWD treasure claim closes at state 70.",409);
        if(rank<=0)throw new GameException("KFWD_TREASURE_RANK_PENDING","Final KFWD ranking is not available.",409);
        var rule=FindTreasure(rank)??throw new GameException("KFWD_TREASURE_NONE","Final ranking has no authoritative KFWD treasure mapping.",409);
        if(claimed)
        {
            await t.RollbackAsync(ct);
            await using var read=await db.DataSource.OpenConnectionAsync(ct);
            return await ReadGrantedTreasureAsync(read,seasonId,playerId,ct)??throw new GameException("KFWD_TREASURE_ALREADY_CLAIMED","KFWD treasure was already claimed.",409);
        }
        var leadership=RandomLegacy(rule.LeaMin,rule.LeaMax);var strength=RandomLegacy(rule.StrMin,rule.StrMax);
        long bagCount;
        await using(var count=new NpgsqlCommand("SELECT (SELECT count(*) FROM player_equipment WHERE player_id=$1)+(SELECT count(*) FROM player_general_treasures WHERE player_id=$1)",c,t))
        {count.Parameters.AddWithValue(playerId);bagCount=Convert.ToInt64(await count.ExecuteScalarAsync(ct));}
        var overflow=bagCount>=maxStore;var sourceKey=$"kfwd-treasure:{seasonId}:{playerId}";var source=manual?"kfwd-rank-treasure-manual":"kfwd-rank-treasure-auto";long id;
        if(!overflow)
        {
            await using var insert=new NpgsqlCommand("INSERT INTO player_general_treasures(player_id,treasure_id,goods_type,quality,leadership,strength,state,source,source_key) VALUES($1,$2,$3,$4,$5,$6,0,$7,$8) ON CONFLICT(source_key) DO UPDATE SET source_key=excluded.source_key RETURNING id",c,t);
            insert.Parameters.AddWithValue(playerId);insert.Parameters.AddWithValue(rule.Tid);insert.Parameters.AddWithValue(rule.GoodsType);insert.Parameters.AddWithValue(rule.Quality);insert.Parameters.AddWithValue(leadership);insert.Parameters.AddWithValue(strength);insert.Parameters.AddWithValue(source);insert.Parameters.AddWithValue(sourceKey);id=Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
        }
        else
        {
            await using var insert=new NpgsqlCommand("INSERT INTO player_general_treasure_overflow(player_id,treasure_id,goods_type,quality,leadership,strength,source,source_key) VALUES($1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT(source_key) DO UPDATE SET source_key=excluded.source_key RETURNING id",c,t);
            insert.Parameters.AddWithValue(playerId);insert.Parameters.AddWithValue(rule.Tid);insert.Parameters.AddWithValue(rule.GoodsType);insert.Parameters.AddWithValue(rule.Quality);insert.Parameters.AddWithValue(leadership);insert.Parameters.AddWithValue(strength);insert.Parameters.AddWithValue(source);insert.Parameters.AddWithValue(sourceKey);id=Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
        }
        await using(var done=new NpgsqlCommand("UPDATE kfwd_rewards SET treasure_claimed=true,updated_at=now() WHERE season_id=$1 AND player_id=$2",c,t)){done.Parameters.AddWithValue(seasonId);done.Parameters.AddWithValue(playerId);await done.ExecuteNonQueryAsync(ct);}
        await t.CommitAsync(ct);
        return new(id,rule.Tid,rule.Name,rule.GoodsType,rule.Quality,leadership,strength,overflow);
    }

    async Task EnsureAutoTreasureMailsAsync(long seasonId,CancellationToken ct)
    {
        var rows=new List<(long PlayerId,int TreasureId)>();
        await using(var c=await db.DataSource.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand(@"WITH granted AS (
 SELECT player_id,treasure_id,source_key FROM player_general_treasures WHERE source='kfwd-rank-treasure-auto' AND source_key LIKE 'kfwd-treasure:'||$1||':%'
 UNION ALL SELECT player_id,treasure_id,source_key FROM player_general_treasure_overflow WHERE source='kfwd-rank-treasure-auto' AND source_key LIKE 'kfwd-treasure:'||$1||':%')
SELECT g.player_id,g.treasure_id FROM granted g
JOIN kfwd_rewards r ON r.season_id=$1 AND r.player_id=g.player_id AND r.treasure_claimed
WHERE NOT EXISTS(SELECT 1 FROM player_mail m WHERE m.recipient_player_id=g.player_id AND m.source_key='kfwd-treasure-auto:'||$1||':'||g.player_id)",c))
        {q.Parameters.AddWithValue(seasonId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add((r.GetInt64(0),r.GetInt32(1)));}
        foreach(var x in rows)
        {
            var info=TreasureTable.FirstOrDefault(t=>t.Tid==x.TreasureId);if(info is null)continue;
            var body=$"跨服擂台争霸赛结束，系统自动帮您领取未领取的宝物{info.Name}";
            await mail.SendAsync(x.PlayerId,RewardMailTitle,body,Array.Empty<MailAttachment>(), $"kfwd-treasure-auto:{seasonId}:{x.PlayerId}",ct);
        }
    }

    static async Task<KfwdGeneralTreasureView?> ReadGrantedTreasureAsync(NpgsqlConnection c,long seasonId,long playerId,CancellationToken ct)
    {
        var key=$"kfwd-treasure:{seasonId}:{playerId}";
        await using var q=new NpgsqlCommand(@"SELECT id,treasure_id,goods_type,quality,leadership,strength,false FROM player_general_treasures WHERE player_id=$1 AND source_key=$2
UNION ALL SELECT id,treasure_id,goods_type,quality,leadership,strength,true FROM player_general_treasure_overflow WHERE player_id=$1 AND source_key=$2 LIMIT 1",c);
        q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(key);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
        var tid=r.GetInt32(1);var info=TreasureTable.FirstOrDefault(t=>t.Tid==tid);return new(r.GetInt64(0),tid,info?.Name??tid.ToString(),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetBoolean(6));
    }

    static int RankAt(int[] ranks,int day)=>ranks.Length>=day?ranks[day-1]:0;
    static int TicketsFor(int day,int rank){if(day<1||day>3||rank<=0)return 0;foreach(var row in DayTicketTable[day-1])if(rank<=row.MaxRank)return row.Tickets;return 0;}
    static TreasureRule? FindTreasure(int rank)=>TreasureTable.FirstOrDefault(x=>x.MinRank<=rank&&rank<=x.MaxRank);
    static int RandomLegacy(int min,int maxExclusive)=>maxExclusive>min?Random.Shared.Next(min,maxExclusive):min;

    sealed record SeasonRewardClock(long Id,int GlobalState,DateTimeOffset BattleOpensAt,int RoundIntervalSeconds,int TotalRounds,int OneDayRoundLimit);
    sealed record TreasureRule(int Gid,int MinRank,int MaxRank,int Tid,int StrMin,int StrMax,int LeaMin,int LeaMax,int GoodsType,int Quality,string Name);
}
