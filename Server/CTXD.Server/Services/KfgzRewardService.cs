using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfgzRoundRewardProvision(long RoundId,long PlayerId,string RewardInfo);
public sealed record KfgzRoundResultProvision(long RoundId,long PlayerId,int GroupId,int LayerId,int SelfCityCount,int OpponentCityCount,int KillRank,int SoloWins,int OccupyCity);
public sealed record KfgzEndRewardProfileProvision(long SeasonId,int ForceId,string RewardInfo);
public sealed record KfgzEndMappingProvision(long SeasonId,int ForceId,int GroupId,int LayerId);
public sealed record KfgzTitleCandidateProvision(long SeasonId,int ForceId,long PlayerId);
public sealed record KfgzRewardView(bool Mapped,long SeasonId,long ReferenceId,int ClaimTimes,long BaseTickets,long NextTickets,long GoldCost,string? Blocker);
public sealed record KfgzEndRewardSlotView(int Slot,int ClaimTimes,int RequiredNationScore,long BaseTickets,long NextTickets,long GoldCost,bool Available);
public sealed record KfgzEndRewardView(bool Mapped,long SeasonId,int NationScore,KfgzEndRewardSlotView[] Slots,string? Blocker);
public sealed record KfgzRewardClaimResult(long Tickets,long GoldCost,int ClaimTimes);
public sealed record KfgzTitleView(long SeasonId,long PlayerId,string PlayerName,string TitleKey);

public sealed class KfgzRewardService(GameDb db,GamePushHub push)
{
    const int MaxClaims=4;
    const string KfgzTitleKey="TITLE_KFGZ_1";

    public async Task ProvisionRoundRewardAsync(KfgzRoundRewardProvision x,CancellationToken ct)
    {
        var components=ParseRoundReward(x.RewardInfo);
        if(components.Length!=5)throw new GameException("KFGZ_REWARD_MAPPING_INVALID","Legacy KFGZ round reward must contain exactly five colon-separated ticket components.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        await SaveRoundRewardAsync(c,t,x.RoundId,x.PlayerId,x.RewardInfo,ct);
        await t.CommitAsync(ct);
    }

    public async Task<string> MaterializeRoundRewardAsync(KfgzRoundResultProvision x,CancellationToken ct)
    {
        if(x.GroupId<=0||x.LayerId<=0||x.SelfCityCount<0||x.OpponentCityCount<0||x.SoloWins<0||x.OccupyCity<0)
            throw new GameException("KFGZ_ROUND_RESULT_INVALID","KFGZ round result contains invalid authoritative dimensions or counters.");

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        string killRankInfo,cityReward;long soloReward,occupyReward;
        await using(var q=new NpgsqlCommand(@"SELECT b.kill_rank_reward_info,b.solo_reward,b.occupy_city_reward,b.city_reward
FROM kfgz_reward m
JOIN kfgz_battle_reward b ON b.id=m.battle_reward_id
WHERE m.group_id=$1 AND m.layer_id=$2",c,t))
        {
            q.Parameters.AddWithValue(x.GroupId);q.Parameters.AddWithValue(x.LayerId);
            await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("KFGZ_REWARD_MAPPING_MISSING","Authoritative KFGZ battle reward mapping is unavailable for this group/layer.",409);
            killRankInfo=r.GetString(0);soloReward=r.GetInt32(1);occupyReward=r.GetInt32(2);cityReward=r.GetString(3);
        }

        var city=ParseCityReward(cityReward);
        var components=new long[]
        {
            checked((long)x.SelfCityCount*city.cityTicket),
            x.SelfCityCount>=x.OpponentCityCount?city.winTicket:city.lostTicket,
            KillRankTicket(killRankInfo,x.KillRank),
            checked(soloReward*x.SoloWins),
            checked(occupyReward*x.OccupyCity)
        };
        var rewardInfo=string.Join(":",components);
        await SaveRoundRewardAsync(c,t,x.RoundId,x.PlayerId,rewardInfo,ct);
        await t.CommitAsync(ct);
        return rewardInfo;
    }

    public async Task ProvisionEndRewardProfileAsync(KfgzEndRewardProfileProvision x,CancellationToken ct)
    {
        if(x.ForceId is <1 or >3)throw new GameException("KFGZ_FORCE_INVALID","KFGZ force must be 1, 2, or 3.");
        ParseEndReward(x.RewardInfo);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await SaveEndRewardProfileAsync(c,x.SeasonId,x.ForceId,x.RewardInfo,ct);
    }

    public async Task<string> MaterializeEndRewardProfileAsync(KfgzEndMappingProvision x,CancellationToken ct)
    {
        if(x.ForceId is <1 or >3)throw new GameException("KFGZ_FORCE_INVALID","KFGZ force must be 1, 2, or 3.");
        if(x.GroupId<=0||x.LayerId<=0)throw new GameException("KFGZ_END_REWARD_MAPPING_INVALID","KFGZ end reward group/layer must be positive.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        string rewardInfo;
        await using(var q=new NpgsqlCommand(@"SELECT e.reward_info
FROM kfgz_reward m
JOIN kfgz_end_reward e ON e.id=m.end_reward_id
WHERE m.group_id=$1 AND m.layer_id=$2",c))
        {
            q.Parameters.AddWithValue(x.GroupId);q.Parameters.AddWithValue(x.LayerId);
            var value=await q.ExecuteScalarAsync(ct);
            if(value is null)throw new GameException("KFGZ_END_REWARD_MAPPING_MISSING","Authoritative KFGZ end reward mapping is unavailable for this group/layer.",409);
            rewardInfo=Convert.ToString(value)!;
        }
        ParseEndReward(rewardInfo);
        await SaveEndRewardProfileAsync(c,x.SeasonId,x.ForceId,rewardInfo,ct);
        return rewardInfo;
    }

    public async Task ProvisionTitleCandidateAsync(KfgzTitleCandidateProvision x,CancellationToken ct)
    {
        if(x.ForceId is <1 or >3)throw new GameException("KFGZ_FORCE_INVALID","KFGZ force must be 1, 2, or 3.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"INSERT INTO kfgz_title_candidates(season_id,force_id,player_id,title_key)
SELECT $1,$2,$3,$4 WHERE EXISTS(SELECT 1 FROM kfgz_signups WHERE season_id=$1 AND player_id=$3 AND force_id=$2)
ON CONFLICT(season_id,force_id) DO UPDATE SET player_id=EXCLUDED.player_id,title_key=EXCLUDED.title_key,updated_at=now()
WHERE NOT EXISTS(SELECT 1 FROM kfgz_titles WHERE season_id=$1 AND force_id=$2)",c);
        q.Parameters.AddWithValue(x.SeasonId);q.Parameters.AddWithValue(x.ForceId);q.Parameters.AddWithValue(x.PlayerId);q.Parameters.AddWithValue(KfgzTitleKey);
        if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFGZ_TITLE_CANDIDATE_INVALID","Title candidate must be a signup of that season/force and cannot replace an issued title.",409);
    }

    public async Task<KfgzRewardView> GetRoundAsync(long player,long roundId,CancellationToken ct)
    {
        await FinalizeEndedSeasonsAsync(ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("SELECT season_id,reward_info,claim_times FROM kfgz_round_rewards WHERE round_id=$1 AND player_id=$2",c);
        q.Parameters.AddWithValue(roundId);q.Parameters.AddWithValue(player);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return new(false,0,roundId,0,0,0,0,"AUTHORITATIVE_ROUND_REWARD_MISSING");
        var season=r.GetInt64(0);var reward=r.GetString(1);var times=r.GetInt16(2);var baseTickets=Sum(ParseRoundReward(reward));
        return new(true,season,roundId,times,baseTickets,times>=MaxClaims?0:Multiply(baseTickets,times),times>=MaxClaims?0:GoldCost(Multiply(baseTickets,times),times),null);
    }

    public async Task<KfgzRewardClaimResult> ClaimRoundAsync(long player,long roundId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        long season;string reward;int times;
        await using(var q=new NpgsqlCommand("SELECT season_id,reward_info,claim_times FROM kfgz_round_rewards WHERE round_id=$1 AND player_id=$2 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(roundId);q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFGZ_REWARD_MAPPING_MISSING","Authoritative KFGZ round reward mapping is unavailable.",409);season=r.GetInt64(0);reward=r.GetString(1);times=r.GetInt16(2);}
        if(times>=MaxClaims)throw new GameException("KFGZ_REWARD_EXHAUSTED","Legacy KFGZ reward has already been claimed four times.",409);
        var tickets=Multiply(Sum(ParseRoundReward(reward)),times);var gold=GoldCost(tickets,times);
        await GrantAsync(c,t,season,player,"round",roundId,times,tickets,gold,false,ct);
        await using(var q=new NpgsqlCommand("UPDATE kfgz_round_rewards SET claim_times=claim_times+1,updated_at=now() WHERE round_id=$1 AND player_id=$2 AND claim_times=$3",c,t)){q.Parameters.AddWithValue(roundId);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(times);if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFGZ_REWARD_CONFLICT","KFGZ reward changed while claiming.",409);}
        await t.CommitAsync(ct);await push.SendAsync(player,"kfgz.reward",new{kind="round",roundId,tickets,goldCost=gold,claimTimes=times+1},ct);return new(tickets,gold,times+1);
    }

    public async Task<KfgzEndRewardView> GetEndAsync(long player,CancellationToken ct)
    {
        await FinalizeEndedSeasonsAsync(ct);await EnsureFinalRewardAsync(player,ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"SELECT f.season_id,f.nation_score,f.slot1_times,f.slot2_times,f.slot3_times,f.slot4_times,p.reward_info
FROM kfgz_final_rewards f JOIN kfgz_end_reward_profiles p ON p.season_id=f.season_id AND p.force_id=f.force_id
WHERE f.player_id=$1 ORDER BY f.season_id DESC LIMIT 1",c);q.Parameters.AddWithValue(player);
        await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return new(false,0,0,[],"AUTHORITATIVE_END_REWARD_MISSING");
        var season=r.GetInt64(0);var score=r.GetInt32(1);var times=new[]{r.GetInt16(2),r.GetInt16(3),r.GetInt16(4),r.GetInt16(5)};var defs=ParseEndReward(r.GetString(6));
        var slots=new KfgzEndRewardSlotView[4];for(var i=0;i<4;i++){var available=score>=defs[i].threshold;var next=available&&times[i]<MaxClaims?Multiply(defs[i].tickets,times[i]):0;slots[i]=new(i+1,times[i],defs[i].threshold,defs[i].tickets,next,next==0?0:GoldCost(next,times[i]),available);}
        return new(true,season,score,slots,null);
    }

    public async Task<KfgzRewardClaimResult> ClaimEndAsync(long player,int slot,CancellationToken ct)
    {
        if(slot is <1 or >4)throw new GameException("KFGZ_END_REWARD_SLOT_INVALID","Legacy KFGZ end reward slot must be 1 through 4.");
        await EnsureFinalRewardAsync(player,ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        long season;int score,force,times;string reward;
        var col=$"slot{slot}_times";
        await using(var q=new NpgsqlCommand($@"SELECT f.season_id,f.nation_score,f.force_id,f.{col},p.reward_info FROM kfgz_final_rewards f JOIN kfgz_end_reward_profiles p ON p.season_id=f.season_id AND p.force_id=f.force_id WHERE f.player_id=$1 ORDER BY f.season_id DESC LIMIT 1 FOR UPDATE OF f",c,t))
        {q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFGZ_END_REWARD_MAPPING_MISSING","Authoritative KFGZ end reward mapping is unavailable.",409);season=r.GetInt64(0);score=r.GetInt32(1);force=r.GetInt16(2);times=r.GetInt16(3);reward=r.GetString(4);}
        if(times>=MaxClaims)throw new GameException("KFGZ_REWARD_EXHAUSTED","Legacy KFGZ reward has already been claimed four times.",409);
        var def=ParseEndReward(reward)[slot-1];if(score<def.threshold)throw new GameException("KFGZ_END_REWARD_SCORE_LOW","Nation score does not meet this legacy KFGZ reward threshold.",409);
        var tickets=Multiply(def.tickets,times);var gold=GoldCost(tickets,times);
        await GrantAsync(c,t,season,player,"end",slot,times,tickets,gold,false,ct);
        await using(var q=new NpgsqlCommand($"UPDATE kfgz_final_rewards SET {col}={col}+1,updated_at=now() WHERE season_id=$1 AND player_id=$2 AND force_id=$3 AND {col}=$4",c,t)){q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(force);q.Parameters.AddWithValue(times);if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFGZ_REWARD_CONFLICT","KFGZ end reward changed while claiming.",409);}
        await t.CommitAsync(ct);await push.SendAsync(player,"kfgz.reward",new{kind="end",slot,tickets,goldCost=gold,claimTimes=times+1},ct);return new(tickets,gold,times+1);
    }

    public async Task<KfgzTitleView[]> TitlesAsync(long player,CancellationToken ct)
    {
        await FinalizeEndedSeasonsAsync(ct);await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var q=new NpgsqlCommand("SELECT season_id,player_id,player_name,title_key FROM kfgz_titles WHERE player_id=$1 ORDER BY season_id DESC",c);q.Parameters.AddWithValue(player);var list=new List<KfgzTitleView>();await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3)));return list.ToArray();
    }

    public async Task FinalizeEndedSeasonsAsync(CancellationToken ct)
    {
        var seasons=new List<long>();await using(var c=await db.DataSource.OpenConnectionAsync(ct)){await using var q=new NpgsqlCommand("SELECT id FROM kfgz_seasons WHERE ends_at<=now() ORDER BY id",c);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))seasons.Add(r.GetInt64(0));}
        foreach(var season in seasons){await AutoIssueRoundAsync(season,ct);await SeedFinalRewardsAsync(season,ct);await AutoIssueEndAsync(season,ct);await IssueTitlesAsync(season,ct);}
    }

    async Task EnsureFinalRewardAsync(long player,CancellationToken ct)
    {
        long season;await using(var c=await db.DataSource.OpenConnectionAsync(ct)){await using var q=new NpgsqlCommand("SELECT season_id FROM kfgz_signups WHERE player_id=$1 ORDER BY season_id DESC LIMIT 1",c);q.Parameters.AddWithValue(player);var v=await q.ExecuteScalarAsync(ct);if(v is null)return;season=Convert.ToInt64(v);}await SeedFinalRewardsAsync(season,ct,player);
    }

    async Task SaveRoundRewardAsync(NpgsqlConnection c,NpgsqlTransaction t,long roundId,long playerId,string rewardInfo,CancellationToken ct)
    {
        long season;
        await using(var q=new NpgsqlCommand("SELECT r.season_id FROM kfgz_rounds r JOIN kfgz_signups s ON s.season_id=r.season_id AND s.player_id=$2 WHERE r.id=$1",c,t))
        {q.Parameters.AddWithValue(roundId);q.Parameters.AddWithValue(playerId);var v=await q.ExecuteScalarAsync(ct);if(v is null)throw new GameException("KFGZ_REWARD_TARGET_INVALID","Player is not signed into the KFGZ season for this round.",404);season=Convert.ToInt64(v);}
        await using var save=new NpgsqlCommand(@"INSERT INTO kfgz_round_rewards(round_id,season_id,player_id,reward_info) VALUES($1,$2,$3,$4)
ON CONFLICT(round_id,player_id) DO UPDATE SET reward_info=EXCLUDED.reward_info,updated_at=now() WHERE kfgz_round_rewards.claim_times=0",c,t);
        save.Parameters.AddWithValue(roundId);save.Parameters.AddWithValue(season);save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(rewardInfo);
        if(await save.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFGZ_REWARD_MAPPING_LOCKED","A claimed KFGZ round reward mapping cannot be replaced.",409);
    }

    async Task SaveEndRewardProfileAsync(NpgsqlConnection c,long seasonId,int forceId,string rewardInfo,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"INSERT INTO kfgz_end_reward_profiles(season_id,force_id,reward_info) SELECT $1,$2,$3 WHERE EXISTS(SELECT 1 FROM kfgz_seasons WHERE id=$1)
ON CONFLICT(season_id,force_id) DO UPDATE SET reward_info=EXCLUDED.reward_info,updated_at=now()
WHERE NOT EXISTS(SELECT 1 FROM kfgz_final_rewards f WHERE f.season_id=$1 AND f.force_id=$2 AND (f.slot1_times>0 OR f.slot2_times>0 OR f.slot3_times>0 OR f.slot4_times>0))",c);
        q.Parameters.AddWithValue(seasonId);q.Parameters.AddWithValue(forceId);q.Parameters.AddWithValue(rewardInfo);
        if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFGZ_END_REWARD_MAPPING_LOCKED","Season/force is missing or its end reward mapping has already been consumed.",409);
    }

    async Task SeedFinalRewardsAsync(long season,CancellationToken ct,long? onlyPlayer=null)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var rows=new List<(long player,int force,int score)>();await using(var q=new NpgsqlCommand(@"SELECT s.player_id,s.force_id,COALESCE((SELECT CASE WHEN r.force1=s.force_id THEN r.side1_cities ELSE r.side2_cities END FROM kfgz_rounds r WHERE r.season_id=s.season_id AND r.state=2 AND (r.force1=s.force_id OR r.force2=s.force_id) ORDER BY r.round_no DESC,r.id DESC LIMIT 1),-1)::int
FROM kfgz_signups s JOIN kfgz_end_reward_profiles p ON p.season_id=s.season_id AND p.force_id=s.force_id WHERE s.season_id=$1 AND ($2::bigint IS NULL OR s.player_id=$2)",c,t)){q.Parameters.AddWithValue(season);q.Parameters.AddWithValue((object?)(onlyPlayer.HasValue?onlyPlayer.Value:null)??DBNull.Value);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add((r.GetInt64(0),r.GetInt16(1),r.GetInt32(2)));}
        foreach(var x in rows.Where(x=>x.score>=0)){await using var q=new NpgsqlCommand("INSERT INTO kfgz_final_rewards(season_id,player_id,force_id,nation_score) VALUES($1,$2,$3,$4) ON CONFLICT(season_id,player_id) DO NOTHING",c,t);q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(x.player);q.Parameters.AddWithValue(x.force);q.Parameters.AddWithValue(x.score);await q.ExecuteNonQueryAsync(ct);}await t.CommitAsync(ct);
    }

    async Task AutoIssueRoundAsync(long season,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var rows=new List<(long round,long player,string reward)>();await using(var q=new NpgsqlCommand("SELECT round_id,player_id,reward_info FROM kfgz_round_rewards WHERE season_id=$1 AND claim_times=0 FOR UPDATE",c,t)){q.Parameters.AddWithValue(season);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add((r.GetInt64(0),r.GetInt64(1),r.GetString(2)));}
        foreach(var x in rows){var tickets=Sum(ParseRoundReward(x.reward));if(tickets>0&&await GrantAsync(c,t,season,x.player,"round-auto",x.round,0,tickets,0,true,ct)){await using var q=new NpgsqlCommand("UPDATE kfgz_round_rewards SET claim_times=1,updated_at=now() WHERE round_id=$1 AND player_id=$2 AND claim_times=0",c,t);q.Parameters.AddWithValue(x.round);q.Parameters.AddWithValue(x.player);await q.ExecuteNonQueryAsync(ct);}}await t.CommitAsync(ct);
    }

    async Task AutoIssueEndAsync(long season,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var rows=new List<(long player,int score,string reward,int[] times)>();await using(var q=new NpgsqlCommand(@"SELECT f.player_id,f.nation_score,p.reward_info,f.slot1_times,f.slot2_times,f.slot3_times,f.slot4_times FROM kfgz_final_rewards f JOIN kfgz_end_reward_profiles p ON p.season_id=f.season_id AND p.force_id=f.force_id WHERE f.season_id=$1 FOR UPDATE OF f",c,t)){q.Parameters.AddWithValue(season);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add((r.GetInt64(0),r.GetInt32(1),r.GetString(2),new[]{r.GetInt16(3),r.GetInt16(4),r.GetInt16(5),r.GetInt16(6)}));}
        foreach(var x in rows){var defs=ParseEndReward(x.reward);for(var i=0;i<4;i++){if(x.times[i]!=0||x.score<defs[i].threshold||defs[i].tickets<=0)continue;if(await GrantAsync(c,t,season,x.player,"end-auto",i+1,0,defs[i].tickets,0,true,ct)){var col=$"slot{i+1}_times";await using var q=new NpgsqlCommand($"UPDATE kfgz_final_rewards SET {col}=1,updated_at=now() WHERE season_id=$1 AND player_id=$2 AND {col}=0",c,t);q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(x.player);await q.ExecuteNonQueryAsync(ct);}}}await t.CommitAsync(ct);
    }

    async Task IssueTitlesAsync(long season,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var q=new NpgsqlCommand(@"INSERT INTO kfgz_titles(season_id,force_id,player_id,player_name,title_key)
SELECT c.season_id,c.force_id,c.player_id,COALESCE(p.display_name,''),c.title_key FROM kfgz_title_candidates c JOIN players p ON p.id=c.player_id WHERE c.season_id=$1 ON CONFLICT(season_id,force_id) DO NOTHING",c);q.Parameters.AddWithValue(season);await q.ExecuteNonQueryAsync(ct);
    }

    async Task<bool> GrantAsync(NpgsqlConnection c,NpgsqlTransaction t,long season,long player,string kind,long reference,int claimNo,long tickets,long gold,bool autoIssue,CancellationToken ct)
    {
        await using(var ledger=new NpgsqlCommand("INSERT INTO kfgz_reward_ledger(season_id,player_id,reward_kind,reward_ref,claim_no,tickets,gold_cost,auto_issue) VALUES($1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT DO NOTHING",c,t)){ledger.Parameters.AddWithValue(season);ledger.Parameters.AddWithValue(player);ledger.Parameters.AddWithValue(kind);ledger.Parameters.AddWithValue(reference);ledger.Parameters.AddWithValue(claimNo);ledger.Parameters.AddWithValue(tickets);ledger.Parameters.AddWithValue(gold);ledger.Parameters.AddWithValue(autoIssue);if(await ledger.ExecuteNonQueryAsync(ct)!=1)return false;}
        if(gold>0)
        {
            await using var q=new NpgsqlCommand(@"UPDATE players
SET sys_gold=GREATEST(sys_gold-$2,0),
    user_gold=user_gold-GREATEST($2-sys_gold,0),
    updated_at=now()
WHERE id=$1 AND sys_gold+user_gold>=$2",c,t);
            q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(gold);
            if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("GOLD_NOT_ENOUGH","Not enough gold for this legacy KFGZ repeat claim.",409);
        }
        if(tickets>0){await using var q=new NpgsqlCommand("INSERT INTO player_tickets(player_id,tickets) VALUES($1,$2) ON CONFLICT(player_id) DO UPDATE SET tickets=player_tickets.tickets+EXCLUDED.tickets,updated_at=now()",c,t);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(tickets);await q.ExecuteNonQueryAsync(ct);}return true;
    }

    static long[] ParseRoundReward(string value)
    {
        if(string.IsNullOrWhiteSpace(value))throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ reward mapping is empty.");var parts=value.Split(':');var result=new long[parts.Length];for(var i=0;i<parts.Length;i++)if(!long.TryParse(parts[i],out result[i])||result[i]<0)throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ reward mapping contains a non-numeric or negative ticket component.");return result;
    }
    static (int threshold,long tickets)[] ParseEndReward(string value)
    {
        if(string.IsNullOrWhiteSpace(value))throw new GameException("KFGZ_END_REWARD_MAPPING_INVALID","KFGZ end reward mapping is empty.");var parts=value.Split(',');if(parts.Length!=4)throw new GameException("KFGZ_END_REWARD_MAPPING_INVALID","Legacy KFGZ end reward must contain exactly four score:tickets entries.");var result=new (int,long)[4];for(var i=0;i<4;i++){var pair=parts[i].Split(':');if(pair.Length!=2||!int.TryParse(pair[0],out var threshold)||threshold<0||!long.TryParse(pair[1],out var tickets)||tickets<0)throw new GameException("KFGZ_END_REWARD_MAPPING_INVALID","KFGZ end reward entry must be non-negative score:tickets.");result[i]=(threshold,tickets);}return result;
    }
    static long KillRankTicket(string value,int killRank)
    {
        if(killRank<=0)return 0;
        foreach(var part in value.Split(','))
        {
            var pair=part.Split(':');
            if(pair.Length!=2||!int.TryParse(pair[0],out var threshold)||threshold<=0||!long.TryParse(pair[1],out var tickets)||tickets<0)throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ kill-rank reward mapping is invalid.");
            if(killRank<=threshold)return tickets;
        }
        throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ kill-rank reward mapping has no terminal bucket.");
    }
    static (long cityTicket,long winTicket,long lostTicket) ParseCityReward(string value)
    {
        long? city=null,win=null,lost=null;
        foreach(var part in value.Split(','))
        {
            var pair=part.Split(':');
            if(pair.Length!=2||!long.TryParse(pair[1],out var amount)||amount<0)throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ city reward mapping is invalid.");
            switch(pair[0]){case "cnum":city=amount;break;case "win":win=amount;break;case "lost":lost=amount;break;default:throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ city reward mapping contains an unknown component.");}
        }
        if(city is null||win is null||lost is null)throw new GameException("KFGZ_REWARD_MAPPING_INVALID","KFGZ city reward mapping is incomplete.");
        return(city.Value,win.Value,lost.Value);
    }
    static long Sum(long[] values){long total=0;foreach(var value in values)total=checked(total+value);return total;}
    static long Multiply(long tickets,int claimTimes)=>claimTimes switch{0=>tickets,1=>tickets,2=>checked(tickets*2),3=>checked(tickets*4),_=>0};
    static long GoldCost(long tickets,int claimTimes)=>claimTimes switch{0=>0,1=>tickets/100,2=>tickets/50,3=>tickets/25,_=>0};
}
