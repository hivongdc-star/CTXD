using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public static partial class EquipmentCompositeService
{
    public static async Task<EquipmentSkillBattleEffect> BattleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CanonicalContent content,
        long playerId,
        int generalId,
        CancellationToken ct)
    {
        CompositeRow? row = null;
        await using (var cmd = new NpgsqlCommand(@"
SELECT id,item_id,composite_type,owner_general_id
FROM player_equipment_composites
WHERE player_id=$1 AND owner_general_id=$2
LIMIT 1", connection, transaction))
        {
            cmd.Parameters.AddWithValue(playerId);
            cmd.Parameters.AddWithValue(generalId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                row = new CompositeRow(r.GetInt64(0), r.GetInt32(1), r.GetInt16(2), r.IsDBNull(3) ? null : r.GetInt32(3));
        }
        if (row is null) return default;

        var catalog = GetCatalog(content);
        if (row.CompositeType == SuitType)
        {
            if (!catalog.SuitsByItemId.TryGetValue(row.ItemId, out var suit))
                throw new GameException("SUIT_DEFINITION_MISSING", "Thiếu dữ liệu Suit legacy.", 500);
            return CoordinateBattleEffect(content, suit, includeSuitRow: true);
        }
        if (row.CompositeType == ProsetType)
        {
            if (!catalog.ProsetsByItemId.TryGetValue(row.ItemId, out var proset) ||
                !catalog.SuitsById.TryGetValue(proset.SetMain, out var mainSuit))
                throw new GameException("PROSET_DEFINITION_MISSING", "Thiếu dữ liệu Proset legacy.", 500);
            var coordinate = CoordinateBattleEffect(content, mainSuit, includeSuitRow: false);
            return coordinate with
            {
                Attack = coordinate.Attack + proset.Attack,
                Defense = coordinate.Defense + proset.Defense,
                Blood = coordinate.Blood + proset.Blood * 3
            };
        }
        return default;
    }

    public static async Task UnequipCompositeForNormalWearAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long playerId,
        int generalId,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
UPDATE player_equipment_composites
SET owner_general_id=NULL,updated_at=now()
WHERE player_id=$1 AND owner_general_id=$2", connection, transaction);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(generalId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static EquipmentSkillBattleEffect CoordinateBattleEffect(CanonicalContent content, SuitDef suit, bool includeSuitRow)
    {
        var attack = 0;
        var defense = 0;
        var blood = 0;
        for (var slot = 1; slot <= 6; slot++)
        {
            var equipment = GetLegacySuitBaseEquipment(content, slot);
            switch (slot)
            {
                case 1:
                case 2:
                    attack += equipment.Attribute;
                    break;
                case 3:
                case 4:
                    defense += equipment.Attribute;
                    break;
                default:
                    blood += equipment.Attribute;
                    break;
            }

            var skill = EquipmentSkillEffectService.Resolve(content, suit.Skills[slot - 1], equipment.SkillLevelMax);
            attack += skill.Attack * 4;
            defense += skill.Defense * 4;
            blood += skill.Blood * 4;
        }

        if (includeSuitRow)
        {
            attack += suit.Attack;
            defense += suit.Defense;
            blood += suit.Blood * 3;
        }
        return new EquipmentSkillBattleEffect(attack, defense, blood, 0, 0, 0, 0);
    }

    static async Task DemountSuitAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CanonicalContent content,
        IPlayerItemInventory itemInventory,
        long playerId,
        CompositeRow row,
        CancellationToken ct)
    {
        var catalog = GetCatalog(content);
        if (!catalog.SuitsByItemId.TryGetValue(row.ItemId, out var suit))
            throw new GameException("SUIT_DEFINITION_MISSING", "Thiếu dữ liệu Suit legacy.", 500);
        var backups = await ReadBackupsAsync(conn, tx, row.Id, ct);
        if (backups.Count != 6 || backups.Any(x => x.GroupIndex != 0) || backups.Select(x => x.SlotIndex).Distinct().Count() != 6)
            throw new GameException("SUIT_BACKUP_INCONSISTENT", "Dữ liệu backup Suit không đủ 6 trang bị.", 409);

        await RequireWarehouseCapacityAsync(conn, tx, playerId, 6, ct);
        await ConsumeGoldLegacyAsync(conn, tx, playerId, suit.UnloadGold, ct);
        foreach (var backup in backups.OrderBy(x => x.SlotIndex))
            await RestoreEquipmentAsync(conn, tx, playerId, backup, ct);
        await DeleteCompositeAsync(conn, tx, playerId, row.Id, ct);
        await GrantBlueprintAsync(content, itemInventory, conn, tx, playerId, row.ItemId, SuitBlueprintType, ct);
    }

    static async Task DemountProsetAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CanonicalContent content,
        IPlayerItemInventory itemInventory,
        long playerId,
        CompositeRow row,
        CancellationToken ct)
    {
        var catalog = GetCatalog(content);
        if (!catalog.ProsetsByItemId.TryGetValue(row.ItemId, out var proset) ||
            !catalog.SuitsById.TryGetValue(proset.SetMain, out var mainSuit) ||
            !catalog.SuitsById.TryGetValue(proset.Set1, out var subSuit))
            throw new GameException("PROSET_DEFINITION_MISSING", "Thiếu dữ liệu Proset legacy.", 500);

        var backups = await ReadBackupsAsync(conn, tx, row.Id, ct);
        if (backups.Count != 12 ||
            backups.Count(x => x.GroupIndex == 0) != 6 ||
            backups.Count(x => x.GroupIndex == 1) != 6 ||
            backups.Where(x => x.GroupIndex == 0).Select(x => x.SlotIndex).Distinct().Count() != 6 ||
            backups.Where(x => x.GroupIndex == 1).Select(x => x.SlotIndex).Distinct().Count() != 6)
            throw new GameException("PROSET_BACKUP_INCONSISTENT", "Dữ liệu backup Proset không đủ 12 trang bị chia 6+6.", 409);

        await RequireWarehouseCapacityAsync(conn, tx, playerId, 2, ct);
        await ConsumeGoldLegacyAsync(conn, tx, playerId, proset.UnloadGold, ct);
        var mainId = await InsertCompositeAsync(conn, tx, playerId, mainSuit.ItemId, SuitType, ct);
        var subId = await InsertCompositeAsync(conn, tx, playerId, subSuit.ItemId, SuitType, ct);
        await ReparentBackupsByGroupAsync(conn, tx, row.Id, mainId, 0, ct);
        await ReparentBackupsByGroupAsync(conn, tx, row.Id, subId, 1, ct);
        await DeleteCompositeAsync(conn, tx, playerId, row.Id, ct);
        await GrantBlueprintAsync(content, itemInventory, conn, tx, playerId, row.ItemId, ProsetBlueprintType, ct);
    }
}
