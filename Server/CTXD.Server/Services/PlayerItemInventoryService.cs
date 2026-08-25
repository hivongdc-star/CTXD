using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public interface IPlayerItemInventory
{
    Task<bool> ConsumeAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long playerId,int itemId,int itemType,int quantity,CancellationToken ct);
    Task GrantAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long playerId,int itemId,int itemType,int quantity,CancellationToken ct);
}

public sealed class PlayerItemInventoryService : IPlayerItemInventory
{
    public async Task<bool> ConsumeAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int itemId,int itemType,int quantity,CancellationToken ct)
    {if(quantity<=0)throw new ArgumentOutOfRangeException(nameof(quantity));await using var cmd=new NpgsqlCommand("UPDATE player_items SET quantity=quantity-$4,updated_at=now() WHERE player_id=$1 AND item_id=$2 AND item_type=$3 AND quantity>=$4 RETURNING quantity",c,t);cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(itemId);cmd.Parameters.AddWithValue(itemType);cmd.Parameters.AddWithValue(quantity);return await cmd.ExecuteScalarAsync(ct)is not null;}
    public async Task GrantAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int itemId,int itemType,int quantity,CancellationToken ct)
    {if(itemId<=0||itemType<0||quantity<=0)throw new ArgumentOutOfRangeException(nameof(quantity));await using var cmd=new NpgsqlCommand("INSERT INTO player_items(player_id,item_id,item_type,quantity) VALUES($1,$2,$3,$4) ON CONFLICT(player_id,item_id,item_type) DO UPDATE SET quantity=player_items.quantity+excluded.quantity,updated_at=now()",c,t);cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(itemId);cmd.Parameters.AddWithValue(itemType);cmd.Parameters.AddWithValue(quantity);await cmd.ExecuteNonQueryAsync(ct);}
}
