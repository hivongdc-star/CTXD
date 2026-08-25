using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record DstqTier(int Gold,int Reward,int ItemId);
public sealed record DstqActivityView(long ActivityId,int Gold,int Level,int NeedGold,int Ticket106,int Ticket107,int Remaining106,int Remaining107,DateTimeOffset EndsAt,DstqTier[] Tiers);

public sealed class DstqActivityService(GameDb db,IPlayerItemInventory items,GamePushHub push)
{
 public async Task RecordGoldSpendAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,int gold,CancellationToken ct)
 {
  if(gold<=0)return;var active=await ActiveAsync(c,t,ct);if(active is null)return;var tiers=Parse(active.Value.rules);int before;
  await using(var q=new NpgsqlCommand("SELECT CASE WHEN activity_id=$2 THEN consume_gold ELSE 0 END FROM player_dstq_activity WHERE player_id=$1 FOR UPDATE",c,t)){q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(active.Value.id);before=Convert.ToInt32(await q.ExecuteScalarAsync(ct)??0);}
  var after=checked(before+gold);var crossed=tiers.Where(x=>x.Gold>before&&x.Gold<=after).ToArray();
  foreach(var tier in crossed){await using var ledger=new NpgsqlCommand("INSERT INTO player_dstq_grants(player_id,activity_id,threshold_gold,item_id,quantity) VALUES($1,$2,$3,$4,$5) ON CONFLICT DO NOTHING",c,t);ledger.Parameters.AddWithValue(player);ledger.Parameters.AddWithValue(active.Value.id);ledger.Parameters.AddWithValue(tier.Gold);ledger.Parameters.AddWithValue(tier.ItemId);ledger.Parameters.AddWithValue(tier.Reward);if(await ledger.ExecuteNonQueryAsync(ct)==1)await items.GrantAsync(c,t,player,tier.ItemId,1,tier.Reward,ct);}
  var n106=crossed.Where(x=>x.ItemId==106).Sum(x=>x.Reward);var n107=crossed.Where(x=>x.ItemId==107).Sum(x=>x.Reward);
  await using var save=new NpgsqlCommand("INSERT INTO player_dstq_activity(player_id,activity_id,consume_gold,ticket_106,ticket_107) VALUES($1,$2,$3,$4,$5) ON CONFLICT(player_id) DO UPDATE SET activity_id=$2,consume_gold=CASE WHEN player_dstq_activity.activity_id=$2 THEN player_dstq_activity.consume_gold+$3 ELSE $3 END,ticket_106=CASE WHEN player_dstq_activity.activity_id=$2 THEN player_dstq_activity.ticket_106+$4 ELSE $4 END,ticket_107=CASE WHEN player_dstq_activity.activity_id=$2 THEN player_dstq_activity.ticket_107+$5 ELSE $5 END,updated_at=now()",c,t);save.Parameters.AddWithValue(player);save.Parameters.AddWithValue(active.Value.id);save.Parameters.AddWithValue(gold);save.Parameters.AddWithValue(n106);save.Parameters.AddWithValue(n107);await save.ExecuteNonQueryAsync(ct);
 }
 public async Task<DstqActivityView> GetAsync(long player,CancellationToken ct)
 {await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var q=new NpgsqlCommand("SELECT id,end_at,params_info FROM scheduled_activities WHERE activity_type=8 AND status=1 AND start_at<=now() AND end_at>now() ORDER BY start_at DESC LIMIT 1",c);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("DSTQ_ACTIVITY_UNAVAILABLE","DSTQ activity is unavailable.");var id=r.GetInt64(0);var end=r.GetFieldValue<DateTimeOffset>(1);var tiers=Parse(r.GetString(2));await r.DisposeAsync();int gold=0,a=0,b=0;await using(var p=new NpgsqlCommand("SELECT consume_gold,ticket_106,ticket_107 FROM player_dstq_activity WHERE player_id=$1 AND activity_id=$2",c)){p.Parameters.AddWithValue(player);p.Parameters.AddWithValue(id);await using var pr=await p.ExecuteReaderAsync(ct);if(await pr.ReadAsync(ct)){gold=pr.GetInt32(0);a=pr.GetInt32(1);b=pr.GetInt32(2);}}var level=tiers.Count(x=>gold>=x.Gold)+1;var next=tiers.FirstOrDefault(x=>gold<x.Gold);return new(id,gold,level,next is null?0:next.Gold-gold,a,b,tiers.Where(x=>x.ItemId==106).Sum(x=>x.Reward)-a,tiers.Where(x=>x.ItemId==107).Sum(x=>x.Reward)-b,end,tiers);}
 static DstqTier[] Parse(string raw)=>raw.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split(',')).Select(x=>new DstqTier(int.Parse(x[0]),int.Parse(x[1]),int.Parse(x[2])==1?106:107)).ToArray();
 static async Task<(long id,string rules)?> ActiveAsync(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT id,params_info FROM scheduled_activities WHERE activity_type=8 AND status=1 AND start_at<=now() AND end_at>now() ORDER BY start_at DESC LIMIT 1",c,t);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?(r.GetInt64(0),r.GetString(1)):null;}
 public Task PushAsync(long player,CancellationToken ct)=>push.SendAsync(player,"activity.updated",new{kind="dstq"},ct);
}
