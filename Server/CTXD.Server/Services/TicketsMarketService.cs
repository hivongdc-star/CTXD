using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record TicketsMarketItemView(int Id,int Tickets,int BuyLevel,int SeeLevel,string Reward,string Pic,int ItemId,int ItemType,string Name,bool Buyable);
public sealed record TicketsMarketView(long Tickets,TicketsMarketItemView[] Goods);
public sealed record TicketsBuyRequest(int Quantity=1);
public sealed record TicketsBuyResult(long Tickets,TicketsMarketItemView Item,int Quantity);

public sealed class TicketsMarketService(GameDb db,CanonicalContent content,IPlayerItemInventory items,GamePushHub push)
{
    sealed class MarketDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int Id{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int Tickets{get;set;}
        [JsonPropertyName("buy_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int BuyLevel{get;set;}
        [JsonPropertyName("see_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int SeeLevel{get;set;}
        public string Reward{get;set;}="";public string Pic{get;set;}="";
    }
    static readonly ConcurrentDictionary<string,IReadOnlyDictionary<int,MarketDef>> Cache=new(StringComparer.OrdinalIgnoreCase);
    readonly IReadOnlyDictionary<int,MarketDef> defs=Cache.GetOrAdd(content.BaseDirectory,dir=>
    {
        var opt=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,NumberHandling=JsonNumberHandling.AllowReadingFromString};
        return (JsonSerializer.Deserialize<MarketDef[]>(File.ReadAllText(Path.Combine(dir,"tickets_market.json")),opt)??throw new InvalidOperationException("Cannot load tickets_market.json.")).ToDictionary(x=>x.Id);
    });

    public static TicketsMarketService FromServices(IServiceProvider services)=>new(
        services.GetRequiredService<GameDb>(),services.GetRequiredService<CanonicalContent>(),services.GetRequiredService<IPlayerItemInventory>(),services.GetRequiredService<GamePushHub>());

    public async Task<TicketsMarketView> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var (level,tickets)=await EnsurePlayerAsync(c,t,playerId,true,ct);var prisonLv=await PrisonLevelAsync(c,t,playerId,ct);
        var goods=new List<TicketsMarketItemView>();
        foreach(var d in defs.Values.OrderBy(x=>x.Id))
        {
            if(level<d.SeeLevel)continue;
            var parsed=ParseReward(d.Reward);if(!SupportedForCurrentRemake(parsed.kind,parsed.value))continue;
            var item=await BuildItemAsync(c,t,playerId,level,prisonLv,d,parsed.kind,parsed.value,ct);if(item is not null)goods.Add(item);
        }
        await t.CommitAsync(ct);return new(tickets,goods.ToArray());
    }

    public async Task<TicketsBuyResult> BuyAsync(long playerId,int marketId,int quantity,CancellationToken ct)
    {
        if(quantity<=0)throw new GameException("TICKETS_QUANTITY_INVALID","Số lượng mua không hợp lệ.");
        if(!defs.TryGetValue(marketId,out var d))throw new GameException("TICKETS_GOOD_MISSING","Vật phẩm Điểm Khoán không tồn tại.",404);
        var parsed=ParseReward(d.Reward);if(!SupportedForCurrentRemake(parsed.kind,parsed.value))throw new GameException("TICKETS_GOOD_DEPENDENCY","Vật phẩm này phụ thuộc hệ thống legacy chưa được port.",409);
        if(parsed.kind=="item"&&quantity!=1)throw new GameException("TICKETS_ITEM_QUANTITY","Vật phẩm bản vẽ chỉ mua một lần.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var (level,tickets)=await EnsurePlayerAsync(c,t,playerId,true,ct);if(level<d.BuyLevel)throw new GameException("TICKETS_LEVEL_LIMIT",$"Cần cấp {d.BuyLevel} để mua.");
        var prisonLv=await PrisonLevelAsync(c,t,playerId,ct);
        var view=await BuildItemAsync(c,t,playerId,level,prisonLv,d,parsed.kind,parsed.value,ct)??throw new GameException("TICKETS_GOOD_HIDDEN","Vật phẩm chưa thể mua ở trạng thái hiện tại.");
        var cost=(long)d.Tickets*quantity;if(tickets<cost)throw new GameException("TICKETS_NOT_ENOUGH","Điểm Khoán không đủ.");
        await using(var spend=new NpgsqlCommand("UPDATE player_tickets SET tickets=tickets-$2,updated_at=now() WHERE player_id=$1 AND tickets>=$2 RETURNING tickets",c,t)){spend.Parameters.AddWithValue(playerId);spend.Parameters.AddWithValue(cost);var raw=await spend.ExecuteScalarAsync(ct);if(raw is null)throw new GameException("TICKETS_NOT_ENOUGH","Điểm Khoán không đủ.");tickets=Convert.ToInt64(raw);}
        if(parsed.kind=="item")
        {
            var item=content.Items[parsed.value];await items.GrantAsync(c,t,playerId,item.Id,item.Type,1,ct);
        }
        else
        {
            var amount=(long)parsed.value*quantity;var column=parsed.kind=="food"?"food":"iron";
            await using var add=new NpgsqlCommand($"UPDATE player_resources SET {column}={column}+$2 WHERE player_id=$1",c,t);add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(amount);await add.ExecuteNonQueryAsync(ct);
        }
        await t.CommitAsync(ct);var result=new TicketsBuyResult(tickets,view,quantity);await push.SendAsync(playerId,"tickets.updated",result,ct);return result;
    }

    public static async Task GrantAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,long amount,CancellationToken ct)
    {
        if(amount<=0)return;await using var add=new NpgsqlCommand("INSERT INTO player_tickets(player_id,tickets) VALUES($1,$2) ON CONFLICT(player_id) DO UPDATE SET tickets=player_tickets.tickets+excluded.tickets,updated_at=now()",c,t);add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(amount);await add.ExecuteNonQueryAsync(ct);
    }

    async Task<TicketsMarketItemView?> BuildItemAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,int level,int prisonLv,MarketDef d,string kind,int value,CancellationToken ct)
    {
        if(kind=="item")
        {
            if(!content.Items.TryGetValue(value,out var item))return null;
            // Exact TicketsService branch for prison drawings (item type 8): only the next
            // prison-level drawing is visible, and an already-owned drawing is hidden.
            if(item.Type==8)
            {
                if(item.Index!=prisonLv+1)return null;
                await using var owned=new NpgsqlCommand("SELECT COALESCE((SELECT quantity FROM player_items WHERE player_id=$1 AND item_id=$2 AND item_type=$3),0)",c,t);owned.Parameters.AddWithValue(player);owned.Parameters.AddWithValue(item.Id);owned.Parameters.AddWithValue(item.Type);if(Convert.ToInt64(await owned.ExecuteScalarAsync(ct))>0)return null;
                return new(d.Id,d.Tickets,d.BuyLevel,d.SeeLevel,d.Reward,d.Pic,item.Id,item.Type,item.Name,level>=d.BuyLevel);
            }
            return null;
        }
        return new(d.Id,d.Tickets,d.BuyLevel,d.SeeLevel,d.Reward,d.Pic,0,0,kind=="food"?"Lương thực":"Sắt",level>=d.BuyLevel);
    }

    static bool SupportedForCurrentRemake(string kind,int value)=>kind is "food" or "iron" || kind=="item"&&value is>=601 and<=605;
    static (string kind,int value) ParseReward(string reward){var p=reward.Split(':',StringSplitOptions.TrimEntries);if(p.Length!=2||!int.TryParse(p[1],out var value))throw new GameException("TICKETS_STATIC_INVALID",$"Reward Điểm Khoán không hợp lệ: {reward}",500);return(p[0].ToLowerInvariant(),value);}
    static async Task<int> PrisonLevelAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT COALESCE((SELECT prison_lv FROM player_prisons WHERE player_id=$1),0)",c,t);q.Parameters.AddWithValue(player);return Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
    static async Task<(int level,long tickets)> EnsurePlayerAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,bool update,CancellationToken ct)
    {
        int level;await using(var p=new NpgsqlCommand($"SELECT level FROM players WHERE id=$1{(update?" FOR UPDATE":"")}",c,t)){p.Parameters.AddWithValue(player);var raw=await p.ExecuteScalarAsync(ct);if(raw is null)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);level=Convert.ToInt32(raw);}
        await using(var ensure=new NpgsqlCommand("INSERT INTO player_tickets(player_id) VALUES($1) ON CONFLICT DO NOTHING",c,t)){ensure.Parameters.AddWithValue(player);await ensure.ExecuteNonQueryAsync(ct);}
        await using var q=new NpgsqlCommand($"SELECT tickets FROM player_tickets WHERE player_id=$1{(update?" FOR UPDATE":"")}",c,t);q.Parameters.AddWithValue(player);return(level,Convert.ToInt64(await q.ExecuteScalarAsync(ct)));
    }
}
