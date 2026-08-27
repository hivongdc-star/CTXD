using System.Text.Json;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public static partial class EquipmentCompositeService
{
    sealed record BackupRow(
        int GroupIndex,
        int SlotIndex,
        int EquipmentId,
        int GoodsType,
        int Level,
        int Quality,
        int Attribute,
        string RefreshAttribute,
        int GemId,
        int QuenchingTimes,
        int QuenchingTimesFree,
        int SpecialSkillId,
        int State,
        int Num);

    static Catalog GetCatalog(CanonicalContent content) => Cache.GetOrAdd(content.BaseDirectory, LoadCatalog);

    static Catalog LoadCatalog(string directory)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var suits = JsonSerializer.Deserialize<SuitDef[]>(
                        File.ReadAllText(Path.Combine(directory, "equip_coordinates.json")), options)
                    ?? throw new InvalidOperationException("Cannot load equip_coordinates.json.");
        var prosets = JsonSerializer.Deserialize<ProsetDef[]>(
                          File.ReadAllText(Path.Combine(directory, "equip_proset.json")), options)
                      ?? throw new InvalidOperationException("Cannot load equip_proset.json.");

        if (suits.Any(x => x.Skills.Length != 6))
            throw new InvalidOperationException("Legacy equip_coordinates requires exactly six skill coordinates per Suit.");
        if (suits.Select(x => x.ItemId).Distinct().Count() != suits.Length ||
            suits.Select(x => x.Id).Distinct().Count() != suits.Length ||
            prosets.Select(x => x.ItemId).Distinct().Count() != prosets.Length)
            throw new InvalidOperationException("Duplicate Suit/Proset canonical id.");

        return new Catalog(
            suits.ToDictionary(x => x.ItemId),
            suits.ToDictionary(x => x.Id),
            prosets.ToDictionary(x => x.ItemId));
    }

    static int RefreshTokenCount(string raw) =>
        raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    static EquipmentDefinition GetLegacySuitBaseEquipment(CanonicalContent content, int slot)
    {
        var item = content.Equipment.Values
            .Where(x => x.Type == slot && x.Quality == 6)
            .OrderByDescending(x => x.SkillNum)
            .ThenBy(x => x.Id)
            .FirstOrDefault();
        return item ?? throw new GameException(
            "SUIT_BASE_EQUIPMENT_MISSING",
            $"Thiếu trang bị Tử legacy cho slot {slot}.",
            500);
    }

    static async Task<List<EquipmentSnapshot>> ReadSuitCandidatesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CanonicalContent content,
        long playerId,
        CancellationToken ct)
    {
        var rows = new List<EquipmentSnapshot>();
        await using var cmd = new NpgsqlCommand(@"
SELECT id,equipment_id,goods_type,level,quality,attribute,refresh_attribute,gem_id,
       quenching_times,quenching_times_free,special_skill_id,state,num
FROM player_equipment
WHERE player_id=$1
  AND owner_general_id IS NULL
  AND goods_type BETWEEN 1 AND 6
  AND quality=6
  AND num=1
ORDER BY goods_type,id
FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new EquipmentSnapshot(
                r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5),
                r.IsDBNull(6) ? "" : r.GetString(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9),
                r.GetInt32(10), r.GetInt32(11), r.GetInt32(12)));
        }
        return rows;
    }

    static async Task<long> InsertCompositeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int itemId,
        int compositeType,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO player_equipment_composites(player_id,item_id,composite_type)
VALUES($1,$2,$3)
RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(itemId);
        cmd.Parameters.AddWithValue(compositeType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    static async Task InsertBackupAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long compositeId,
        int groupIndex,
        int slotIndex,
        EquipmentSnapshot row,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO player_equipment_composite_backup(
  composite_id,group_index,slot_index,equipment_id,goods_type,level,quality,attribute,
  refresh_attribute,gem_id,quenching_times,quenching_times_free,special_skill_id,state,num)
VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)", conn, tx);
        cmd.Parameters.AddWithValue(compositeId);
        cmd.Parameters.AddWithValue(groupIndex);
        cmd.Parameters.AddWithValue(slotIndex);
        cmd.Parameters.AddWithValue(row.EquipmentId);
        cmd.Parameters.AddWithValue(row.GoodsType);
        cmd.Parameters.AddWithValue(row.Level);
        cmd.Parameters.AddWithValue(row.Quality);
        cmd.Parameters.AddWithValue(row.Attribute);
        cmd.Parameters.AddWithValue(row.RefreshAttribute);
        cmd.Parameters.AddWithValue(row.GemId);
        cmd.Parameters.AddWithValue(row.QuenchingTimes);
        cmd.Parameters.AddWithValue(row.QuenchingTimesFree);
        cmd.Parameters.AddWithValue(row.SpecialSkillId);
        cmd.Parameters.AddWithValue(row.State);
        cmd.Parameters.AddWithValue(row.Num);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task DeleteEquipmentAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        long equipmentInstanceId,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM player_equipment WHERE player_id=$1 AND id=$2", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(equipmentInstanceId);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new GameException("EQUIPMENT_STATE_CHANGED", "Trang bị thành phần đã thay đổi.", 409);
    }

    static async Task<CompositeRow?> ReadFirstSuitAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int itemId,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
SELECT id,item_id,composite_type,owner_general_id
FROM player_equipment_composites
WHERE player_id=$1 AND item_id=$2 AND composite_type=10 AND owner_general_id IS NULL
ORDER BY id
LIMIT 1
FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(itemId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new CompositeRow(r.GetInt64(0), r.GetInt32(1), r.GetInt16(2), r.IsDBNull(3) ? null : r.GetInt32(3));
    }

    static async Task RequireBackupCountAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long compositeId,
        int expected,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
SELECT count(*),count(*) FILTER (WHERE group_index=0)
FROM player_equipment_composite_backup
WHERE composite_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(compositeId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        if (r.GetInt64(0) != expected || r.GetInt64(1) != expected)
            throw new GameException("SUIT_BACKUP_INCONSISTENT", "Dữ liệu backup Suit không đủ 6 trang bị.", 409);
    }

    static async Task ReparentBackupsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long fromCompositeId,
        long toCompositeId,
        int targetGroupIndex,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
UPDATE player_equipment_composite_backup
SET composite_id=$2,group_index=$3
WHERE composite_id=$1 AND group_index=0", conn, tx);
        cmd.Parameters.AddWithValue(fromCompositeId);
        cmd.Parameters.AddWithValue(toCompositeId);
        cmd.Parameters.AddWithValue(targetGroupIndex);
        if (await cmd.ExecuteNonQueryAsync(ct) != 6)
            throw new GameException("SUIT_BACKUP_INCONSISTENT", "Không thể chuyển đủ 6 backup Suit vào Proset.", 409);
    }

    static async Task ReparentBackupsByGroupAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long fromCompositeId,
        long toCompositeId,
        int sourceGroupIndex,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
UPDATE player_equipment_composite_backup
SET composite_id=$2,group_index=0
WHERE composite_id=$1 AND group_index=$3", conn, tx);
        cmd.Parameters.AddWithValue(fromCompositeId);
        cmd.Parameters.AddWithValue(toCompositeId);
        cmd.Parameters.AddWithValue(sourceGroupIndex);
        if (await cmd.ExecuteNonQueryAsync(ct) != 6)
            throw new GameException("PROSET_BACKUP_INCONSISTENT", "Không thể tách đúng 6 backup Proset về Suit.", 409);
    }

    static async Task DeleteCompositeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        long compositeId,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM player_equipment_composites WHERE player_id=$1 AND id=$2", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(compositeId);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new GameException("COMPOSITE_STATE_CHANGED", "Suit/Proset đã thay đổi.", 409);
    }

    static async Task<CompositeRow?> ReadCompositeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        long compositeId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($@"
SELECT id,item_id,composite_type,owner_general_id
FROM player_equipment_composites
WHERE player_id=$1 AND id=$2{(forUpdate ? " FOR UPDATE" : "")}", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(compositeId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new CompositeRow(r.GetInt64(0), r.GetInt32(1), r.GetInt16(2), r.IsDBNull(3) ? null : r.GetInt32(3));
    }

    static async Task<List<BackupRow>> ReadBackupsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        long compositeId,
        CancellationToken ct)
    {
        var rows = new List<BackupRow>();
        await using var cmd = new NpgsqlCommand($@"
SELECT group_index,slot_index,equipment_id,goods_type,level,quality,attribute,refresh_attribute,
       gem_id,quenching_times,quenching_times_free,special_skill_id,state,num
FROM player_equipment_composite_backup
WHERE composite_id=$1
ORDER BY group_index,slot_index{(tx is null ? "" : " FOR UPDATE")}", conn, tx);
        cmd.Parameters.AddWithValue(compositeId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new BackupRow(
                r.GetInt16(0), r.GetInt16(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5),
                r.GetInt32(6), r.IsDBNull(7) ? "" : r.GetString(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10),
                r.GetInt32(11), r.GetInt32(12), r.GetInt32(13)));
        }
        return rows;
    }

    static async Task RestoreEquipmentAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        BackupRow row,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO player_equipment(
  player_id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,
  gem_id,quenching_times,quenching_times_free,special_skill_id,state,num)
VALUES($1,$2,$3,$4,$5,$6,NULL,$7,$8,$9,$10,$11,$12,$13)", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(row.EquipmentId);
        cmd.Parameters.AddWithValue(row.GoodsType);
        cmd.Parameters.AddWithValue(row.Level);
        cmd.Parameters.AddWithValue(row.Quality);
        cmd.Parameters.AddWithValue(row.Attribute);
        cmd.Parameters.AddWithValue(row.RefreshAttribute);
        cmd.Parameters.AddWithValue(row.GemId);
        cmd.Parameters.AddWithValue(row.QuenchingTimes);
        cmd.Parameters.AddWithValue(row.QuenchingTimesFree);
        cmd.Parameters.AddWithValue(row.SpecialSkillId);
        cmd.Parameters.AddWithValue(row.State);
        cmd.Parameters.AddWithValue(row.Num);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task RequireWarehouseCapacityAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int additionalEntries,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
SELECT p.max_store_num,
       (SELECT count(*) FROM player_equipment e WHERE e.player_id=p.id) +
       (SELECT count(*) FROM player_equipment_composites c WHERE c.player_id=p.id)
FROM players p
WHERE p.id=$1
FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
        var max = r.GetInt32(0);
        var current = r.GetInt64(1);
        if (current + additionalEntries > max)
            throw new GameException("WAREHOUSE_FULL", "Kho trang bị không đủ chỗ để tháo Suit/Proset.");
    }

    static async Task RequireTech48Async(
        TechnologyEffectService technology,
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        CancellationToken ct)
    {
        if (await technology.GetCompletedIntEffectAsync(playerId, SuitTechKey, 0, ct, conn, tx) <= 0)
            throw new GameException("EQUIPMENT_SUIT_TECH_LOCKED", "Chưa mở công nghệ Suit/Proset.");
    }

    static async Task RequireBlueprintAsync(
        CanonicalContent content,
        IPlayerItemInventory itemInventory,
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int itemId,
        int expectedItemType,
        CancellationToken ct)
    {
        if (!content.Items.TryGetValue(itemId, out var definition) || definition.Type != expectedItemType)
            throw new GameException("COMPOSITE_BLUEPRINT_DEFINITION_MISSING", "Thiếu dữ liệu bản vẽ Suit/Proset.", 500);
        if (!await itemInventory.ConsumeAsync(conn, tx, playerId, itemId, expectedItemType, 1, ct))
            throw new GameException("COMPOSITE_BLUEPRINT_MISSING", "Không có bản vẽ Suit/Proset cần thiết.");
    }

    static async Task GrantBlueprintAsync(
        CanonicalContent content,
        IPlayerItemInventory itemInventory,
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int itemId,
        int expectedItemType,
        CancellationToken ct)
    {
        if (!content.Items.TryGetValue(itemId, out var definition) || definition.Type != expectedItemType)
            throw new GameException("COMPOSITE_BLUEPRINT_DEFINITION_MISSING", "Thiếu dữ liệu bản vẽ Suit/Proset.", 500);
        await itemInventory.GrantAsync(conn, tx, playerId, itemId, expectedItemType, 1, ct);
    }

    static async Task ConsumeGoldLegacyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int cost,
        CancellationToken ct)
    {
        if (cost <= 0) return;
        long sysGold;
        long userGold;
        await using (var read = new NpgsqlCommand(
                         "SELECT sys_gold,user_gold FROM players WHERE id=$1 FOR UPDATE", conn, tx))
        {
            read.Parameters.AddWithValue(playerId);
            await using var r = await read.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
            sysGold = r.GetInt64(0);
            userGold = r.GetInt64(1);
        }
        if (sysGold + userGold < cost)
            throw new GameException("GOLD_NOT_ENOUGH", "Hoàng kim không đủ.");

        var newSysGold = Math.Max(0, sysGold - cost);
        var remainder = Math.Max(0, cost - sysGold);
        var newUserGold = userGold - remainder;
        await using var update = new NpgsqlCommand(
            "UPDATE players SET sys_gold=$2,user_gold=$3,updated_at=now() WHERE id=$1", conn, tx);
        update.Parameters.AddWithValue(playerId);
        update.Parameters.AddWithValue(newSysGold);
        update.Parameters.AddWithValue(newUserGold);
        if (await update.ExecuteNonQueryAsync(ct) != 1)
            throw new GameException("PLAYER_STATE_CHANGED", "Tài nguyên người chơi đã thay đổi.", 409);
    }

    static async Task<EquipmentCompositeInventoryView> ReadViewAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        CanonicalContent content,
        long playerId,
        CancellationToken ct)
    {
        int max;
        int count;
        await using (var totals = new NpgsqlCommand(@"
SELECT p.max_store_num,
       (SELECT count(*) FROM player_equipment e WHERE e.player_id=p.id) +
       (SELECT count(*) FROM player_equipment_composites c WHERE c.player_id=p.id)
FROM players p
WHERE p.id=$1", conn, tx))
        {
            totals.Parameters.AddWithValue(playerId);
            await using var r = await totals.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
            max = r.GetInt32(0);
            count = Convert.ToInt32(r.GetInt64(1));
        }

        var composites = new List<CompositeRow>();
        await using (var cmd = new NpgsqlCommand(@"
SELECT id,item_id,composite_type,owner_general_id
FROM player_equipment_composites
WHERE player_id=$1
ORDER BY owner_general_id NULLS FIRST,composite_type,item_id,id", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                composites.Add(new CompositeRow(r.GetInt64(0), r.GetInt32(1), r.GetInt16(2), r.IsDBNull(3) ? null : r.GetInt32(3)));
        }

        var catalog = GetCatalog(content);
        var views = new List<EquipmentCompositeView>(composites.Count);
        foreach (var row in composites)
        {
            var backups = await ReadBackupsAsync(conn, tx, row.Id, ct);
            var backupIds = backups.OrderBy(x => x.GroupIndex).ThenBy(x => x.SlotIndex).Select(x => x.EquipmentId).ToArray();
            if (row.CompositeType == SuitType)
            {
                if (!catalog.SuitsByItemId.TryGetValue(row.ItemId, out var suit))
                    throw new GameException("SUIT_DEFINITION_MISSING", "Thiếu dữ liệu Suit legacy.", 500);
                views.Add(new EquipmentCompositeView(
                    row.Id, row.ItemId, row.CompositeType, suit.Name, suit.Pic, row.OwnerGeneralId,
                    suit.Attack, suit.Defense, suit.Blood, suit.Skills.ToArray(), [], backupIds));
            }
            else if (row.CompositeType == ProsetType)
            {
                if (!catalog.ProsetsByItemId.TryGetValue(row.ItemId, out var proset) ||
                    !catalog.SuitsById.TryGetValue(proset.SetMain, out var mainSuit) ||
                    !catalog.SuitsById.TryGetValue(proset.Set1, out var subSuit))
                    throw new GameException("PROSET_DEFINITION_MISSING", "Thiếu dữ liệu Proset legacy.", 500);
                views.Add(new EquipmentCompositeView(
                    row.Id, row.ItemId, row.CompositeType, proset.Name, proset.Pic, row.OwnerGeneralId,
                    proset.Attack, proset.Defense, proset.Blood, mainSuit.Skills.ToArray(),
                    [mainSuit.ItemId, subSuit.ItemId], backupIds));
            }
        }
        return new EquipmentCompositeInventoryView(count, max, views);
    }
}
