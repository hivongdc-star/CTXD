using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

/// <summary>
/// Player equipment runtime translated from the legacy StoreHouse/EquipService slice.
/// Only the inventory/wear/unwear/sell behaviors needed by the current remake milestone live here;
/// forging, gems, quenching and binding remain separate future systems.
/// </summary>
public sealed class EquipmentInventoryService(
    GameDb db,
    CanonicalContent content,
    TutorialService tutorial,
    ResourceProductionService resources)
{
    public async Task<InventoryResponse> GetAsync(long playerId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        var max = await ReadMaxStoreNumAsync(conn, null, playerId, ct);
        var items = await ReadAllAsync(conn, null, playerId, ct);
        return new InventoryResponse(items.Count, max, items);
    }

    public async Task<EquipEquipmentResponse> EquipAsync(long playerId, long instanceId, int generalId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var item = await ReadRowAsync(conn, tx, playerId, instanceId, forUpdate: true, ct)
                   ?? throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị.", 404);
        var generalType = await ReadGeneralTypeAsync(conn, tx, playerId, generalId, ct);
        EnsureCompatible(item.GoodsType, generalType);

        // A general can wear one item per goods_type. Legacy changeEquip swaps the previous StoreHouse owner.
        EquipmentRow? replaced = null;
        await using (var find = new NpgsqlCommand(@"
SELECT id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,gem_id,quenching_times,state,num
FROM player_equipment
WHERE player_id=$1 AND owner_general_id=$2 AND goods_type=$3 AND id<>$4
FOR UPDATE", conn, tx))
        {
            find.Parameters.AddWithValue(playerId);
            find.Parameters.AddWithValue(generalId);
            find.Parameters.AddWithValue(item.GoodsType);
            find.Parameters.AddWithValue(instanceId);
            await using var r = await find.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) replaced = ReadEquipmentRow(r);
        }

        if (replaced is not null)
        {
            await using var unwear = new NpgsqlCommand(
                "UPDATE player_equipment SET owner_general_id=NULL,updated_at=now() WHERE player_id=$1 AND id=$2", conn, tx);
            unwear.Parameters.AddWithValue(playerId);
            unwear.Parameters.AddWithValue(replaced.Id);
            await unwear.ExecuteNonQueryAsync(ct);
        }

        await using (var wear = new NpgsqlCommand(@"
UPDATE player_equipment SET owner_general_id=$3,updated_at=now()
WHERE player_id=$1 AND id=$2", conn, tx))
        {
            wear.Parameters.AddWithValue(playerId);
            wear.Parameters.AddWithValue(instanceId);
            wear.Parameters.AddWithValue(generalId);
            await wear.ExecuteNonQueryAsync(ct);
        }

        // TaskRequestEquipOn observes the persisted inventory state rather than trusting the event alone.
        await tutorial.TryCompleteAsync(conn, tx, playerId, "equip_on", [], ct);

        var updated = await ReadRowAsync(conn, tx, playerId, instanceId, forUpdate: false, ct)
                      ?? throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị.", 404);
        var response = new EquipEquipmentResponse(ToView(updated), replaced is null ? null : ToView(replaced with { OwnerGeneralId = null }));
        await tx.CommitAsync(ct);
        return response;
    }

    public async Task<PlayerEquipmentView> UnequipAsync(long playerId, long instanceId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var item = await ReadRowAsync(conn, tx, playerId, instanceId, forUpdate: true, ct)
                   ?? throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị.", 404);

        if (item.OwnerGeneralId is not null)
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE player_equipment SET owner_general_id=NULL,updated_at=now() WHERE player_id=$1 AND id=$2", conn, tx);
            cmd.Parameters.AddWithValue(playerId);
            cmd.Parameters.AddWithValue(instanceId);
            await cmd.ExecuteNonQueryAsync(ct);
            item = item with { OwnerGeneralId = null };
        }

        await tx.CommitAsync(ct);
        return ToView(item);
    }

    public async Task<SellEquipmentResponse> SellAsync(long playerId, long instanceId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        int playerLevel;
        int maxStoreNum;
        await using (var p = new NpgsqlCommand("SELECT level,max_store_num FROM players WHERE id=$1 FOR UPDATE", conn, tx))
        {
            p.Parameters.AddWithValue(playerId);
            await using var r = await p.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
            playerLevel = r.GetInt32(0);
            maxStoreNum = r.GetInt32(1);
        }
        // Exact legacy EquipService.sellGoods gate for equipment.
        if (playerLevel < 18) throw new GameException("SELL_LEVEL_LOCKED", "Cấp 18 mới có thể bán trang bị.");

        var item = await ReadRowAsync(conn, tx, playerId, instanceId, forUpdate: true, ct)
                   ?? throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị.", 404);
        if (item.OwnerGeneralId is not null)
            throw new GameException("EQUIPMENT_IN_USE", "Trang bị đang được tướng sử dụng.");
        if (!content.Equipment.TryGetValue(item.EquipmentId, out var def))
            throw new GameException("EQUIPMENT_DEFINITION_MISSING", "Thiếu dữ liệu trang bị.", 500);

        // Accrue before adding sale copper. Legacy sell uses ignore-max; do not clamp the sale proceeds to warehouse capacity.
        var before = await resources.AccrueAndGetAsync(playerId, ct, conn, tx);
        var gained = Math.Max(0L, (long)def.CopperSold * Math.Max(1, item.Num));

        await using (var del = new NpgsqlCommand("DELETE FROM player_equipment WHERE player_id=$1 AND id=$2", conn, tx))
        {
            del.Parameters.AddWithValue(playerId);
            del.Parameters.AddWithValue(instanceId);
            if (await del.ExecuteNonQueryAsync(ct) == 0)
                throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị.", 404);
        }
        await using (var add = new NpgsqlCommand("UPDATE player_resources SET copper=copper+$2 WHERE player_id=$1", conn, tx))
        {
            add.Parameters.AddWithValue(playerId);
            add.Parameters.AddWithValue(gained);
            await add.ExecuteNonQueryAsync(ct);
        }

        if (item.GoodsType <= 6)
            await tutorial.TryCompleteAsync(conn, tx, playerId, "sell_equip", [], ct);

        var nowItemNum = await CountInventoryAsync(conn, tx, playerId, ct);
        var after = before with { Copper = before.Copper + gained };
        await tx.CommitAsync(ct);
        return new SellEquipmentResponse(gained, after, nowItemNum, maxStoreNum);
    }

    async Task<int> ReadGeneralTypeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int generalId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT general_type FROM player_generals WHERE player_id=$1 AND general_id=$2", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(generalId);
        var value = await cmd.ExecuteScalarAsync(ct);
        if (value is null) throw new GameException("GENERAL_NOT_OWNED", "Bạn chưa sở hữu võ tướng này.", 404);
        return Convert.ToInt32(value);
    }

    static void EnsureCompatible(int goodsType, int generalType)
    {
        var equipmentGeneralType = goodsType switch
        {
            >= 1 and <= 6 => 2,  // military equipment -> military general
            >= 7 and <= 12 => 1, // civil equipment -> civil general
            _ => 0
        };
        if (equipmentGeneralType == 0 || equipmentGeneralType != generalType)
            throw new GameException("EQUIPMENT_TYPE_MISMATCH", "Loại trang bị không phù hợp với tướng.");
    }

    async Task<List<PlayerEquipmentView>> ReadAllAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long playerId, CancellationToken ct)
    {
        var result = new List<PlayerEquipmentView>();
        await using var cmd = new NpgsqlCommand(@"
SELECT id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,gem_id,quenching_times,state,num
FROM player_equipment WHERE player_id=$1 ORDER BY owner_general_id NULLS FIRST,goods_type,id", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(ToView(ReadEquipmentRow(r)));
        return result;
    }

    async Task<EquipmentRow?> ReadRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, long instanceId, bool forUpdate, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($@"
SELECT id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,gem_id,quenching_times,state,num
FROM player_equipment WHERE player_id=$1 AND id=$2{(forUpdate ? " FOR UPDATE" : "")}", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(instanceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadEquipmentRow(r) : null;
    }

    static EquipmentRow ReadEquipmentRow(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5),
        r.IsDBNull(6) ? null : r.GetInt32(6), r.IsDBNull(7) ? "" : r.GetString(7), r.GetInt32(8),
        r.GetInt32(9), r.GetInt32(10), r.GetInt32(11));

    PlayerEquipmentView ToView(EquipmentRow row)
    {
        if (!content.Equipment.TryGetValue(row.EquipmentId, out var e))
            return new PlayerEquipmentView(row.Id, row.EquipmentId, $"Equipment {row.EquipmentId}", "", row.GoodsType,
                row.Quality, row.Level, row.Attribute, row.OwnerGeneralId, row.RefreshAttribute, row.GemId,
                row.QuenchingTimes, row.State, row.Num, 0);
        return new PlayerEquipmentView(row.Id, row.EquipmentId, e.Name, e.Pic, row.GoodsType, row.Quality, row.Level,
            row.Attribute, row.OwnerGeneralId, row.RefreshAttribute, row.GemId, row.QuenchingTimes, row.State,
            row.Num, e.CopperSold);
    }

    static async Task<int> ReadMaxStoreNumAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT max_store_num FROM players WHERE id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        var value = await cmd.ExecuteScalarAsync(ct);
        if (value is null) throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
        return Convert.ToInt32(value);
    }

    static async Task<int> CountInventoryAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM player_equipment WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    sealed record EquipmentRow(
        long Id, int EquipmentId, int GoodsType, int Level, int Quality, int Attribute, int? OwnerGeneralId,
        string RefreshAttribute, int GemId, int QuenchingTimes, int State, int Num);
}
