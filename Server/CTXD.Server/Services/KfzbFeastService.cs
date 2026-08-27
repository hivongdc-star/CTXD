using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record KfzbFeastOrganizerProvision(int Rank,long PlayerId);
public sealed record KfzbFeastJoinRequest(int Rank,int CardType);
public sealed record KfzbFeastParticipantView(long PlayerId,string Name,int ForceId,int? TitleId,int? Tickets);
public sealed record KfzbFeastRoomView(long RoomId,int Rank,int State,bool Drink,DateTimeOffset ExpiresAt,KfzbFeastParticipantView[] Participants);
public sealed record KfzbFeastDrinkResult(int GoldSpent,int DrinkNum);

public sealed class KfzbFeastService(GameDb db,GamePushHub push,DstqActivityService dstq)
{
    public async Task ProvisionOrganizerAsync(KfzbFeastOrganizerProvision x,CancellationToken ct)
    {
        if(x.Rank is <1 or >32)throw new GameException("KFZB_FEAST_RANK_INVALID","Legacy Feast organizer rank must be 1..32.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("INSERT INTO kfzb_feast_organizers(season_id,rank,player_id) SELECT id,$1,$2 FROM kfzb_seasons ORDER BY season_no DESC LIMIT 1 ON CONFLICT(season_id,rank) DO UPDATE SET player_id=excluded.player_id,updated_at=now()",c);
        q.Parameters.AddWithValue(x.Rank);q.Parameters.AddWithValue(x.PlayerId);
        if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFZB_INACTIVE","No KFZB season is available.",404);
    }

    public async Task<KfzbFeastDrinkResult> BuyDrinkAsync(long player,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var season=await FeastSeasonAsync(c,t,ct);
        await using(var host=new NpgsqlCommand("SELECT 1 FROM kfzb_feast_organizers WHERE season_id=$1 AND player_id=$2 FOR UPDATE",c,t)){host.Parameters.AddWithValue(season);host.Parameters.AddWithValue(player);if(await host.ExecuteScalarAsync(ct)is null)throw new GameException("KFZB_FEAST_NOT_HOST","Only an authoritative Feast organizer may buy drink.",403);}
        await using(var init=new NpgsqlCommand("INSERT INTO kfzb_spectator_state(season_id,player_id) VALUES($1,$2) ON CONFLICT DO NOTHING",c,t)){init.Parameters.AddWithValue(season);init.Parameters.AddWithValue(player);await init.ExecuteNonQueryAsync(ct);}
        await using(var pay=new NpgsqlCommand("UPDATE players SET sys_gold=sys_gold-LEAST(sys_gold,500),user_gold=user_gold-GREATEST(0,500-sys_gold),updated_at=now() WHERE id=$1 AND sys_gold+user_gold>=500",c,t)){pay.Parameters.AddWithValue(player);if(await pay.ExecuteNonQueryAsync(ct)!=1)throw new GameException("KFZB_FEAST_GOLD_NOT_ENOUGH","Not enough gold for Feast drink.");}
        int drink;await using(var q=new NpgsqlCommand("UPDATE kfzb_spectator_state SET drink_num=drink_num+500,updated_at=now() WHERE season_id=$1 AND player_id=$2 RETURNING drink_num",c,t)){q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(player);drink=Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
        await dstq.RecordGoldSpendAsync(c,t,player,500,ct);await t.CommitAsync(ct);
        await push.SendAsync(player,"kfzb.feast",new{reason="drinkBought",drinkNum=drink},ct);return new(500,drink);
    }

    public async Task<KfzbFeastRoomView> JoinAsync(long player,KfzbFeastJoinRequest x,CancellationToken ct)
    {
        if(x.Rank is <1 or >16||x.CardType is not(1 or 2))throw new GameException("KFZB_FEAST_JOIN_INVALID","Legacy Feast room rank must be 1..16 and card type 1 or 2.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var season=await FeastSeasonAsync(c,t,ct);
        await using(var l=new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)",c,t)){l.Parameters.AddWithValue(player);await l.ExecuteNonQueryAsync(ct);}
        await ExpireAsync(c,t,season,ct);
        long? active;await using(var q=new NpgsqlCommand("SELECT p.room_id FROM kfzb_feast_participants p JOIN kfzb_feast_rooms r ON r.id=p.room_id WHERE r.season_id=$1 AND p.player_id=$2 AND r.state=1 ORDER BY p.joined_at DESC LIMIT 1",c,t)){q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(player);active=await q.ExecuteScalarAsync(ct)as long?;}
        if(active.HasValue){var existing=await ReadRoomAsync(c,t,active.Value,ct);await t.CommitAsync(ct);return existing;}
        await using(var init=new NpgsqlCommand("INSERT INTO kfzb_spectator_state(season_id,player_id) VALUES($1,$2) ON CONFLICT DO NOTHING",c,t)){init.Parameters.AddWithValue(season);init.Parameters.AddWithValue(player);await init.ExecuteNonQueryAsync(ct);}
        await using(var card=new NpgsqlCommand(x.CardType==1?"SELECT free_feast_cards FROM kfzb_spectator_state WHERE season_id=$1 AND player_id=$2 FOR UPDATE":"SELECT gold_feast_cards FROM kfzb_spectator_state WHERE season_id=$1 AND player_id=$2 FOR UPDATE",c,t)){card.Parameters.AddWithValue(season);card.Parameters.AddWithValue(player);if(Convert.ToInt32(await card.ExecuteScalarAsync(ct))<=0)throw new GameException("KFZB_FEAST_NO_CARD","No selected Feast card remains.",409);}
        await using(var l=new NpgsqlCommand("SELECT pg_advisory_xact_lock($1,$2)",c,t)){l.Parameters.AddWithValue((int)(season%int.MaxValue));l.Parameters.AddWithValue(x.Rank);await l.ExecuteNonQueryAsync(ct);}
        long room;bool buff;await using(var q=new NpgsqlCommand("SELECT r.id,r.buff FROM kfzb_feast_rooms r WHERE r.season_id=$1 AND r.rank=$2 AND r.state=1 AND r.expires_at>now() AND (SELECT count(*) FROM kfzb_feast_participants p WHERE p.room_id=r.id)<10 ORDER BY r.created_at DESC LIMIT 1 FOR UPDATE",c,t)){q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(x.Rank);await using var r=await q.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){room=r.GetInt64(0);buff=r.GetBoolean(1);}else{room=0;buff=false;}}
        if(room==0){await using var q=new NpgsqlCommand("SELECT o.player_id,COALESCE(s.drink_num,0)>o.drink_used FROM kfzb_feast_organizers o LEFT JOIN kfzb_spectator_state s ON s.season_id=o.season_id AND s.player_id=o.player_id WHERE o.season_id=$1 AND o.rank=$2 FOR UPDATE OF o",c,t);q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(x.Rank);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFZB_FEAST_ORGANIZER_MISSING","Coordinator has not synchronized this Feast rank.",409);buff=r.GetBoolean(1);await r.CloseAsync();await using var create=new NpgsqlCommand("INSERT INTO kfzb_feast_rooms(id,season_id,rank,buff,expires_at) VALUES((nextval('kfzb_feast_room_seq')::bigint<<8)|$2,$1,$2,$3,now()+interval '3 minutes') RETURNING id",c,t);create.Parameters.AddWithValue(season);create.Parameters.AddWithValue(x.Rank);create.Parameters.AddWithValue(buff);room=Convert.ToInt64(await create.ExecuteScalarAsync(ct));}
        short force;await using(var q=new NpgsqlCommand("SELECT force_id FROM players WHERE id=$1",c,t)){q.Parameters.AddWithValue(player);force=Convert.ToInt16(await q.ExecuteScalarAsync(ct));}
        await using(var q=new NpgsqlCommand("INSERT INTO kfzb_feast_participants(room_id,player_id,card_type,force_id) VALUES($1,$2,$3,$4)",c,t)){q.Parameters.AddWithValue(room);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(x.CardType);q.Parameters.AddWithValue(force);await q.ExecuteNonQueryAsync(ct);}
        if(buff){await using var q=new NpgsqlCommand("UPDATE kfzb_feast_organizers SET drink_used=drink_used+1,updated_at=now() WHERE season_id=$1 AND rank=$2",c,t);q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(x.Rank);await q.ExecuteNonQueryAsync(ct);}
        var count=0;await using(var q=new NpgsqlCommand("SELECT count(*)::int FROM kfzb_feast_participants WHERE room_id=$1",c,t)){q.Parameters.AddWithValue(room);count=Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
        if(count==10)await ResolveAsync(c,t,room,ct);
        var view=await ReadRoomAsync(c,t,room,ct);await t.CommitAsync(ct);
        foreach(var p in view.Participants)await push.SendAsync(p.PlayerId,"kfzb.feast",new{reason=view.State==2?"resolved":"roomUpdated",room=view},ct);
        return view;
    }

    public async Task<KfzbFeastRoomView> RoomAsync(long player,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var season=await FeastSeasonAsync(c,t,ct);await ExpireAsync(c,t,season,ct);
        long room;await using(var q=new NpgsqlCommand("SELECT p.room_id FROM kfzb_feast_participants p JOIN kfzb_feast_rooms r ON r.id=p.room_id WHERE r.season_id=$1 AND p.player_id=$2 ORDER BY p.joined_at DESC LIMIT 1",c,t)){q.Parameters.AddWithValue(season);q.Parameters.AddWithValue(player);room=Convert.ToInt64(await q.ExecuteScalarAsync(ct)??throw new GameException("KFZB_FEAST_ROOM_MISSING","Player has no Feast room.",404));}
        var view=await ReadRoomAsync(c,t,room,ct);await t.CommitAsync(ct);return view;
    }

    static async Task<long> FeastSeasonAsync(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT id,COALESCE(feast_opens_at,date_trunc('day',ends_at)+interval '1 day'),COALESCE(feast_ends_at,date_trunc('day',ends_at)+interval '2 days') FROM kfzb_seasons ORDER BY season_no DESC LIMIT 1",c,t);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFZB_INACTIVE","No KFZB season is available.",404);var id=r.GetInt64(0);var now=DateTimeOffset.UtcNow;if(now<r.GetFieldValue<DateTimeOffset>(1)||now>=r.GetFieldValue<DateTimeOffset>(2))throw new GameException("KFZB_FEAST_CLOSED","KFZB Feast is not open.",409);return id;
    }

    static async Task ExpireAsync(NpgsqlConnection c,NpgsqlTransaction t,long season,CancellationToken ct){await using var q=new NpgsqlCommand("UPDATE kfzb_feast_rooms SET state=3 WHERE season_id=$1 AND state=1 AND expires_at<=now()",c,t);q.Parameters.AddWithValue(season);await q.ExecuteNonQueryAsync(ct);}

    static async Task ResolveAsync(NpgsqlConnection c,NpgsqlTransaction t,long room,CancellationToken ct)
    {
        var counts=new int[4];await using(var q=new NpgsqlCommand("SELECT force_id,count(*)::int FROM kfzb_feast_participants WHERE room_id=$1 GROUP BY force_id",c,t)){q.Parameters.AddWithValue(room);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))if(r.GetInt16(0)is >=1 and <=3)counts[r.GetInt16(0)]=r.GetInt32(1);}
        var special=counts.Contains(9)&&counts.Contains(1);
        var people=new[]{1,1,1,1,1,1,2,3,4,5,6,7};var ext=new[]{0,0,600,700,800,900,1000,1200};
        var peopleRows=new List<(long player,int card,int force)>();await using(var q=new NpgsqlCommand("SELECT player_id,card_type,force_id FROM kfzb_feast_participants WHERE room_id=$1 AND settled_at IS NULL FOR UPDATE",c,t)){q.Parameters.AddWithValue(room);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))peopleRows.Add((r.GetInt64(0),r.GetInt16(1),r.GetInt16(2)));}
        foreach(var p in peopleRows){var title=special&&counts[p.force]==1?7:people[counts[p.force]];int baseTicket;await using(var use=new NpgsqlCommand(p.card==1?"UPDATE kfzb_spectator_state SET free_feast_cards=free_feast_cards-1 WHERE season_id=(SELECT season_id FROM kfzb_feast_rooms WHERE id=$1) AND player_id=$2 AND free_feast_cards>0":"UPDATE kfzb_spectator_state SET gold_feast_cards=gold_feast_cards-1 WHERE season_id=(SELECT season_id FROM kfzb_feast_rooms WHERE id=$1) AND player_id=$2 AND gold_feast_cards>0",c,t)){use.Parameters.AddWithValue(room);use.Parameters.AddWithValue(p.player);if(await use.ExecuteNonQueryAsync(ct)!=1)continue;}bool buff;await using(var q=new NpgsqlCommand("SELECT buff FROM kfzb_feast_rooms WHERE id=$1",c,t)){q.Parameters.AddWithValue(room);buff=Convert.ToBoolean(await q.ExecuteScalarAsync(ct));}baseTicket=p.card==1?(buff?800:500):(buff?4000:2500);var tickets=baseTicket+ext[title];var key=$"kfzb-feast:{room}:{p.player}";await using(var q=new NpgsqlCommand("WITH g AS (INSERT INTO player_ticket_grants(grant_key,player_id,amount,source) VALUES($1,$2,$3,'kfzb-feast') ON CONFLICT DO NOTHING RETURNING 1) INSERT INTO player_tickets(player_id,tickets) SELECT $2,$3 FROM g ON CONFLICT(player_id) DO UPDATE SET tickets=player_tickets.tickets+excluded.tickets,updated_at=now();UPDATE kfzb_feast_participants SET title_id=$4,tickets=$3,settled_at=now() WHERE room_id=$5 AND player_id=$2",c,t)){q.Parameters.AddWithValue(key);q.Parameters.AddWithValue(p.player);q.Parameters.AddWithValue(tickets);q.Parameters.AddWithValue(title);q.Parameters.AddWithValue(room);await q.ExecuteNonQueryAsync(ct);}}
        await using var finish=new NpgsqlCommand("UPDATE kfzb_feast_rooms SET state=2,resolved_at=now() WHERE id=$1 AND state=1",c,t);finish.Parameters.AddWithValue(room);await finish.ExecuteNonQueryAsync(ct);
    }

    static async Task<KfzbFeastRoomView> ReadRoomAsync(NpgsqlConnection c,NpgsqlTransaction t,long room,CancellationToken ct)
    {
        int rank,state;bool buff;DateTimeOffset expires;await using(var q=new NpgsqlCommand("SELECT rank,state,buff,expires_at FROM kfzb_feast_rooms WHERE id=$1",c,t)){q.Parameters.AddWithValue(room);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("KFZB_FEAST_ROOM_MISSING","Feast room does not exist.",404);rank=r.GetInt32(0);state=r.GetInt16(1);buff=r.GetBoolean(2);expires=r.GetFieldValue<DateTimeOffset>(3);}
        var list=new List<KfzbFeastParticipantView>();await using(var q=new NpgsqlCommand("SELECT p.player_id,pl.name,p.force_id,p.title_id,p.tickets FROM kfzb_feast_participants p JOIN players pl ON pl.id=p.player_id WHERE p.room_id=$1 ORDER BY p.joined_at,p.player_id",c,t)){q.Parameters.AddWithValue(room);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt16(2),r.IsDBNull(3)?null:r.GetInt32(3),r.IsDBNull(4)?null:r.GetInt32(4)));}
        return new(room,rank,state,buff,expires,list.ToArray());
    }
}
