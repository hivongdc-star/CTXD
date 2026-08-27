using System.Collections.Concurrent;
using System.Text.Json;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record CourtesyEventView(long Id,int Type,long PlayerId,string PlayerName,int PlayerPic,int PlayerLv,int EventId,int State);
public sealed record CourtesyStateView(bool Open,int LiYiDu,int MaxLiYiDu,bool LiShangWangLai,IReadOnlyList<CourtesyEventView> Events);
public sealed record CourtesyHandleView(int LiYiDu,string? RewardType,int RewardNum,CourtesyStateView State);

public static class CourtesyService
{
    public const int MaxLiYiDu=784000;
    public const int OpenBoxExternalEvent=7;
    public const int OpenBoxStaticEventId=13;
    public const string PendingNotificationChannel="courtesy_pending";
    static readonly ConcurrentDictionary<long,int> RecommendCount=new();

    sealed record Profile(long Id,string Name,int Pic,int Level,int ForceId,int LiYiDu);
    sealed record Candidate(Profile Profile,int EventId,string SourceKey,int RecommendCount);

    public static async Task AddPlayerEventAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int externalEventId,int parameter,string sourceKey,CancellationToken ct)
    {
        if(externalEventId!=OpenBoxExternalEvent||parameter!=0)
            throw new GameException("COURTESY_EVENT_UNSUPPORTED",$"Unsupported Courtesy event {externalEventId},{parameter}.",500);
        if(!GamePushHub.IsPlayerConnected(playerId))return; // legacy Players.getPlayer(): offline players do not enter CourtesyManager.
        var actor=await NeedHandleProfileAsync(c,t,playerId,ct);
        if(actor is null)return;

        // Legacy addNewCourtesyEvent first publishes/overwrites the actor's newlyEventId, then the actor tries to claim another player's offer.
        await using(var offer=new NpgsqlCommand(@"INSERT INTO courtesy_offers(source_player_id,event_id,source_key,created_at)
VALUES($1,$2,$3,now())
ON CONFLICT(source_player_id) DO UPDATE SET event_id=EXCLUDED.event_id,source_key=EXCLUDED.source_key,created_at=EXCLUDED.created_at",c,t))
        {
            offer.Parameters.AddWithValue(playerId);offer.Parameters.AddWithValue(OpenBoxStaticEventId);offer.Parameters.AddWithValue(sourceKey);
            await offer.ExecuteNonQueryAsync(ct);
        }
        await TryClaimOfferAsync(c,t,actor,ct);
    }

    public static async Task<CourtesyStateView> GetAsync(GameDb db,long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var open=await EnsureStateIfOpenAsync(c,t,playerId,ct);
        var liYiDu=0;
        if(open)
        {
            await using var points=new NpgsqlCommand("SELECT li_yi_du FROM player_courtesy WHERE player_id=$1",c,t);points.Parameters.AddWithValue(playerId);
            liYiDu=Convert.ToInt32(await points.ExecuteScalarAsync(ct));
        }
        var events=open?await EventsAsync(c,t,playerId,ct):Array.Empty<CourtesyEventView>();
        await t.CommitAsync(ct);
        return new(open,liYiDu,MaxLiYiDu,open,events);
    }

    public static async Task<CourtesyHandleView> HandleAsync(GameDb db,long playerId,long courtesyEventId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        if(!await EnsureStateIfOpenAsync(c,t,playerId,ct))throw new GameException("COURTESY_CLOSED","Courtesy is not unlocked.",409);

        long counterparty;int type,eventId,state;
        await using(var cmd=new NpgsqlCommand(@"SELECT type,counterparty_player_id,event_id,state
FROM courtesy_events WHERE id=$1 AND player_id=$2 FOR UPDATE",c,t))
        {
            cmd.Parameters.AddWithValue(courtesyEventId);cmd.Parameters.AddWithValue(playerId);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("COURTESY_EVENT_NOT_FOUND","Courtesy event does not exist.",404);
            type=r.GetInt16(0);counterparty=r.GetInt64(1);eventId=r.GetInt32(2);state=r.GetInt16(3);
        }
        if(state!=1)throw new GameException("COURTESY_EVENT_HANDLED","Courtesy event was already handled.",409);
        if(eventId!=OpenBoxStaticEventId)throw new GameException("COURTESY_STATIC_UNSUPPORTED",$"Unsupported Courtesy static event {eventId}.",500);

        var point=0;string? rewardType=null;var rewardNum=0;
        if(type==1)
        {
            point=await AddPointsAsync(c,t,playerId,10,ct);
            await MarkHandledAsync(c,t,courtesyEventId,ct);
            // Legacy creates the reply only while the source player is still online/module-open/<=36/not maxed.
            if(GamePushHub.IsPlayerConnected(counterparty)&&await NeedHandleProfileAsync(c,t,counterparty,ct) is not null)
            {
                var handler=await ProfileAsync(c,t,playerId,ct)??throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
                await InsertEventAsync(c,t,counterparty,2,handler,eventId,$"reply:{courtesyEventId}",ct);
                await NotifyPendingAsync(c,t,counterparty,ct);
            }
        }
        else if(type==2)
        {
            await MarkHandledAsync(c,t,courtesyEventId,ct);
            await using var wood=new NpgsqlCommand("UPDATE player_resources SET wood=wood+1000 WHERE player_id=$1",c,t);wood.Parameters.AddWithValue(playerId);
            if(await wood.ExecuteNonQueryAsync(ct)!=1)throw new GameException("PLAYER_RESOURCE_NOT_FOUND","Player resource row does not exist.",500);
            rewardType="lumber";rewardNum=1000;
        }
        else throw new GameException("COURTESY_TYPE_INVALID",$"Invalid Courtesy event type {type}.",500);

        var liYiDu=await CurrentPointsAsync(c,t,playerId,ct);
        var events=await EventsAsync(c,t,playerId,ct);
        await t.CommitAsync(ct);
        return new(point,rewardType,rewardNum,new(true,liYiDu,MaxLiYiDu,true,events));
    }

    static async Task TryClaimOfferAsync(NpgsqlConnection c,NpgsqlTransaction t,Profile actor,CancellationToken ct)
    {
        var candidates=new List<Candidate>();
        await using(var cmd=new NpgsqlCommand(@"SELECT o.source_player_id,o.event_id,o.source_key,COALESCE(p.display_name,''),p.pic,p.level,p.force_id,COALESCE(pc.li_yi_du,0)
FROM courtesy_offers o
JOIN players p ON p.id=o.source_player_id
JOIN player_functions f ON f.player_id=p.id AND f.function_id=67
LEFT JOIN player_courtesy pc ON pc.player_id=p.id
WHERE p.force_id=$1 AND p.id<>$2 AND p.level BETWEEN $3 AND $4 AND COALESCE(pc.li_yi_du,0)<$5
ORDER BY o.source_player_id
FOR UPDATE OF o SKIP LOCKED",c,t))
        {
            cmd.Parameters.AddWithValue(actor.ForceId);cmd.Parameters.AddWithValue(actor.Id);cmd.Parameters.AddWithValue(actor.Level-5);cmd.Parameters.AddWithValue(actor.Level+5);cmd.Parameters.AddWithValue(MaxLiYiDu);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))
            {
                var id=r.GetInt64(0);if(!GamePushHub.IsPlayerConnected(id))continue;
                var profile=new Profile(id,r.GetString(3),r.GetInt32(4),r.GetInt32(5),r.GetInt16(6),r.GetInt32(7));
                candidates.Add(new(profile,r.GetInt32(1),r.GetString(2),RecommendCount.GetValueOrDefault(id)));
            }
        }
        if(candidates.Count==0)return;
        var chosen=ChooseCandidate(candidates);
        await InsertEventAsync(c,t,actor.Id,1,chosen.Profile,chosen.EventId,$"offer:{chosen.Profile.Id}:{chosen.SourceKey}",ct);
        await using(var consume=new NpgsqlCommand("DELETE FROM courtesy_offers WHERE source_player_id=$1 AND source_key=$2",c,t))
        {consume.Parameters.AddWithValue(chosen.Profile.Id);consume.Parameters.AddWithValue(chosen.SourceKey);if(await consume.ExecuteNonQueryAsync(ct)!=1)return;}
        RecommendCount.AddOrUpdate(chosen.Profile.Id,1,static(_,v)=>v+1);
        await NotifyPendingAsync(c,t,actor.Id,ct);
    }

    static Candidate ChooseCandidate(IReadOnlyList<Candidate> candidates)
    {
        if(candidates.Count==1)return candidates[0];
        var sum=candidates.Sum(x=>(long)x.RecommendCount);
        if(sum<=0)return candidates[0]; // exact Java weighting collapses to the first map iteration candidate when every recommendCount is zero.
        var rand=(long)(Random.Shared.NextDouble()*sum);long acc=0;var divisor=candidates.Count-1;
        foreach(var candidate in candidates)
        {
            acc+=(sum-candidate.RecommendCount)/divisor;
            if(acc>=rand)return candidate;
        }
        return candidates[^1];
    }

    static async Task InsertEventAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int type,Profile counterparty,int eventId,string sourceKey,CancellationToken ct)
    {
        await using(var insert=new NpgsqlCommand(@"INSERT INTO courtesy_events(player_id,type,counterparty_player_id,player_name,player_pic,player_level,event_id,state,source_key)
VALUES($1,$2,$3,$4,$5,$6,$7,1,$8)
ON CONFLICT(player_id,source_key) WHERE source_key IS NOT NULL DO NOTHING",c,t))
        {
            insert.Parameters.AddWithValue(playerId);insert.Parameters.AddWithValue((short)type);insert.Parameters.AddWithValue(counterparty.Id);insert.Parameters.AddWithValue(counterparty.Name);
            insert.Parameters.AddWithValue(counterparty.Pic);insert.Parameters.AddWithValue(counterparty.Level);insert.Parameters.AddWithValue(eventId);insert.Parameters.AddWithValue(sourceKey);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using var trim=new NpgsqlCommand(@"DELETE FROM courtesy_events WHERE id IN(
SELECT id FROM courtesy_events WHERE player_id=$1 ORDER BY id DESC OFFSET 4)",c,t);trim.Parameters.AddWithValue(playerId);await trim.ExecuteNonQueryAsync(ct);
    }

    static async Task MarkHandledAsync(NpgsqlConnection c,NpgsqlTransaction t,long eventId,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("UPDATE courtesy_events SET state=2,handled_at=now() WHERE id=$1 AND state=1",c,t);cmd.Parameters.AddWithValue(eventId);
        if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new GameException("COURTESY_EVENT_HANDLED","Courtesy event was already handled.",409);
    }

    static async Task<int> AddPointsAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int amount,CancellationToken ct)
    {
        var current=await CurrentPointsAsync(c,t,playerId,ct);var add=Math.Max(0,Math.Min(amount,MaxLiYiDu-current));
        if(add>0){await using var cmd=new NpgsqlCommand("UPDATE player_courtesy SET li_yi_du=li_yi_du+$2 WHERE player_id=$1",c,t);cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(add);await cmd.ExecuteNonQueryAsync(ct);}
        return add;
    }

    static async Task<int> CurrentPointsAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("SELECT li_yi_du FROM player_courtesy WHERE player_id=$1",c,t);cmd.Parameters.AddWithValue(playerId);
        var value=await cmd.ExecuteScalarAsync(ct);return value is null or DBNull?0:Convert.ToInt32(value);
    }

    static async Task<bool> EnsureStateIfOpenAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using(var open=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=67)",c,t))
        {open.Parameters.AddWithValue(playerId);if(!Convert.ToBoolean(await open.ExecuteScalarAsync(ct)))return false;}
        await using var add=new NpgsqlCommand("INSERT INTO player_courtesy(player_id,li_yi_du,reward_info) VALUES($1,0,NULL) ON CONFLICT(player_id) DO NOTHING",c,t);add.Parameters.AddWithValue(playerId);await add.ExecuteNonQueryAsync(ct);return true;
    }

    static async Task<Profile?> NeedHandleProfileAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        if(!await EnsureStateIfOpenAsync(c,t,playerId,ct))return null;
        var profile=await ProfileAsync(c,t,playerId,ct);if(profile is null||profile.Level>36||profile.LiYiDu>=MaxLiYiDu)return null;return profile;
    }

    static async Task<Profile?> ProfileAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand(@"SELECT p.id,COALESCE(p.display_name,''),p.pic,p.level,p.force_id,COALESCE(pc.li_yi_du,0)
FROM players p LEFT JOIN player_courtesy pc ON pc.player_id=p.id WHERE p.id=$1",c,t);cmd.Parameters.AddWithValue(playerId);
        await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;return new(r.GetInt64(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetInt16(4),r.GetInt32(5));
    }

    static async Task<CourtesyEventView[]> EventsAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        var result=new List<CourtesyEventView>();await using var cmd=new NpgsqlCommand(@"SELECT id,type,counterparty_player_id,player_name,player_pic,player_level,event_id,state
FROM courtesy_events WHERE player_id=$1 ORDER BY id DESC LIMIT 4",c,t);cmd.Parameters.AddWithValue(playerId);await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))result.Add(new(r.GetInt64(0),r.GetInt16(1),r.GetInt64(2),r.GetString(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6),r.GetInt16(7)));return result.ToArray();
    }

    static async Task NotifyPendingAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        var payload=JsonSerializer.Serialize(new{playerId,liShangWangLai=true});await using var cmd=new NpgsqlCommand($"SELECT pg_notify('{PendingNotificationChannel}',$1)",c,t);cmd.Parameters.AddWithValue(payload);await cmd.ExecuteNonQueryAsync(ct);
    }
}
