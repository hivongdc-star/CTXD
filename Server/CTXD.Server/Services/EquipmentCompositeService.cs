using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record EquipmentCompositeView(
    long InstanceId,
    int ItemId,
    int CompositeType,
    string Name,
    string Pic,
    int? OwnerGeneralId,
    int Attack,
    int Defense,
    int Blood,
    int[] RequiredSpecialSkillIds,
    int[] ComponentSuitItemIds,
    int[] BackupEquipmentIds);

public sealed record EquipmentCompositeInventoryView(
    int NowItemNum,
    int MaxStoreNum,
    IReadOnlyList<EquipmentCompositeView> Composites);

public static partial class EquipmentCompositeService
{
    const int SuitType = 10;
    const int ProsetType = 14;
    const int SuitBlueprintType = 6;
    const int ProsetBlueprintType = 11;
    const int SuitTechKey = 48;

    sealed class SuitDef
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string Name { get; set; } = "";
        public string Pic { get; set; } = "";
        public int[] Skills { get; set; } = [];
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Blood { get; set; }
        public int LoadGold { get; set; }
        public int UnloadGold { get; set; }
    }

    sealed class ProsetDef
    {
        public int ItemId { get; set; }
        public string Name { get; set; } = "";
        public string Pic { get; set; } = "";
        public int SetMain { get; set; }
        public int Set1 { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Blood { get; set; }
        public int LoadGold { get; set; }
        public int UnloadGold { get; set; }
    }

    sealed record Catalog(
        IReadOnlyDictionary<int, SuitDef> SuitsByItemId,
        IReadOnlyDictionary<int, SuitDef> SuitsById,
        IReadOnlyDictionary<int, ProsetDef> ProsetsByItemId);

    sealed record EquipmentSnapshot(
        long Id,
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

    sealed record CompositeRow(long Id, int ItemId, int CompositeType, int? OwnerGeneralId);

    static readonly ConcurrentDictionary<string, Catalog> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<EquipmentCompositeInventoryView> GetAsync(GameDb db, CanonicalContent content, long playerId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        return await ReadViewAsync(conn, null, content, playerId, ct);
    }

    public static async Task<EquipmentCompositeInventoryView> CompoundSuitAsync(
        GameDb db,
        CanonicalContent content,
        TechnologyEffectService technology,
        IPlayerItemInventory itemInventory,
        long playerId,
        int itemId,
        CancellationToken ct)
    {
        var catalog = GetCatalog(content);
        if (!catalog.SuitsByItemId.TryGetValue(itemId, out var suit))
            throw new GameException("SUIT_DEFINITION_MISSING", "Thiếu dữ liệu Suit legacy.", 404);

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await RequireTech48Async(technology, conn, tx, playerId, ct);
        await RequireBlueprintAsync(content, itemInventory, conn, tx, playerId, itemId, SuitBlueprintType, ct);

        var candidates = await ReadSuitCandidatesAsync(conn, tx, content, playerId, ct);
        var selected = new List<EquipmentSnapshot>(6);
        for (var slot = 1; slot <= 6; slot++)
        {
            var requiredSkill = suit.Skills[slot - 1];
            var baseEquipment = GetLegacySuitBaseEquipment(content, slot);
            var chosen = candidates.FirstOrDefault(x =>
                !selected.Any(y => y.Id == x.Id) &&
                x.GoodsType == slot &&
                x.SpecialSkillId == requiredSkill &&
                RefreshTokenCount(x.RefreshAttribute) == baseEquipment.SkillNum + 1);
            if (chosen is null)
                throw new GameException("SUIT_COMPONENT_MISSING", $"Thiếu trang bị slot {slot} có specialSkillId={requiredSkill}.");
            selected.Add(chosen);
        }

        var compositeId = await InsertCompositeAsync(conn, tx, playerId, itemId, SuitType, ct);
        for (var i = 0; i < selected.Count; i++)
        {
            await InsertBackupAsync(conn, tx, compositeId, 0, i + 1, selected[i], ct);
            await DeleteEquipmentAsync(conn, tx, playerId, selected[i].Id, ct);
        }

        await tx.CommitAsync(ct);
        return await GetAsync(db, content, playerId, ct);
    }

    public static async Task<EquipmentCompositeInventoryView> CompoundProsetAsync(
        GameDb db,
        CanonicalContent content,
        TechnologyEffectService technology,
        IPlayerItemInventory itemInventory,
        long playerId,
        int itemId,
        CancellationToken ct)
    {
        var catalog = GetCatalog(content);
        if (!catalog.ProsetsByItemId.TryGetValue(itemId, out var proset) ||
            !catalog.SuitsById.TryGetValue(proset.SetMain, out var mainSuit) ||
            !catalog.SuitsById.TryGetValue(proset.Set1, out var subSuit))
            throw new GameException("PROSET_DEFINITION_MISSING", "Thiếu dữ liệu Proset legacy.", 404);

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await RequireTech48Async(technology, conn, tx, playerId, ct);
        await RequireBlueprintAsync(content, itemInventory, conn, tx, playerId, itemId, ProsetBlueprintType, ct);

        var main = await ReadFirstSuitAsync(conn, tx, playerId, mainSuit.ItemId, ct)
                   ?? throw new GameException("PROSET_MAIN_SUIT_MISSING", "Thiếu Suit chính để hợp thành Proset.");
        var sub = await ReadFirstSuitAsync(conn, tx, playerId, subSuit.ItemId, ct)
                  ?? throw new GameException("PROSET_SUB_SUIT_MISSING", "Thiếu Suit phụ để hợp thành Proset.");
        if (main.Id == sub.Id)
            throw new GameException("PROSET_COMPONENT_INVALID", "Hai Suit thành phần không hợp lệ.", 409);

        await RequireBackupCountAsync(conn, tx, main.Id, 6, ct);
        await RequireBackupCountAsync(conn, tx, sub.Id, 6, ct);

        var prosetId = await InsertCompositeAsync(conn, tx, playerId, itemId, ProsetType, ct);
        await ReparentBackupsAsync(conn, tx, main.Id, prosetId, 0, ct);
        await ReparentBackupsAsync(conn, tx, sub.Id, prosetId, 1, ct);
        await DeleteCompositeAsync(conn, tx, playerId, main.Id, ct);
        await DeleteCompositeAsync(conn, tx, playerId, sub.Id, ct);

        await tx.CommitAsync(ct);
        return await GetAsync(db, content, playerId, ct);
    }

    public static async Task<EquipmentCompositeInventoryView> DemountAsync(
        GameDb db,
        CanonicalContent content,
        TechnologyEffectService technology,
        IPlayerItemInventory itemInventory,
        long playerId,
        long compositeId,
        CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var row = await ReadCompositeAsync(conn, tx, playerId, compositeId, true, ct)
                  ?? throw new GameException("COMPOSITE_NOT_FOUND", "Không tìm thấy Suit/Proset.", 404);
        if (row.OwnerGeneralId is not null)
            throw new GameException("COMPOSITE_IN_USE", "Suit/Proset đang được tướng sử dụng.");

        if (row.CompositeType == SuitType)
            await DemountSuitAsync(conn, tx, content, itemInventory, playerId, row, ct);
        else if (row.CompositeType == ProsetType)
        {
            await RequireTech48Async(technology, conn, tx, playerId, ct);
            await DemountProsetAsync(conn, tx, content, itemInventory, playerId, row, ct);
        }
        else
            throw new GameException("COMPOSITE_TYPE_INVALID", "Loại Suit/Proset không hợp lệ.", 409);

        await tx.CommitAsync(ct);
        return await GetAsync(db, content, playerId, ct);
    }

    public static async Task<EquipmentCompositeInventoryView> EquipAsync(
        GameDb db,
        CanonicalContent content,
        long playerId,
        long compositeId,
        int generalId,
        CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var row = await ReadCompositeAsync(conn, tx, playerId, compositeId, true, ct)
                  ?? throw new GameException("COMPOSITE_NOT_FOUND", "Không tìm thấy Suit/Proset.", 404);

        await using (var general = new NpgsqlCommand(
                         "SELECT general_type FROM player_generals WHERE player_id=$1 AND general_id=$2", conn, tx))
        {
            general.Parameters.AddWithValue(playerId);
            general.Parameters.AddWithValue(generalId);
            var type = await general.ExecuteScalarAsync(ct);
            if (type is null) throw new GameException("GENERAL_NOT_OWNED", "Bạn chưa sở hữu võ tướng này.", 404);
            if (Convert.ToInt32(type) != 2)
                throw new GameException("EQUIPMENT_TYPE_MISMATCH", "Suit/Proset chỉ dành cho võ tướng.");
        }

        await using (var normal = new NpgsqlCommand(
                         "UPDATE player_equipment SET owner_general_id=NULL,updated_at=now() WHERE player_id=$1 AND owner_general_id=$2", conn, tx))
        {
            normal.Parameters.AddWithValue(playerId);
            normal.Parameters.AddWithValue(generalId);
            await normal.ExecuteNonQueryAsync(ct);
        }
        await using (var old = new NpgsqlCommand(
                         "UPDATE player_equipment_composites SET owner_general_id=NULL,updated_at=now() WHERE player_id=$1 AND owner_general_id=$2 AND id<>$3", conn, tx))
        {
            old.Parameters.AddWithValue(playerId);
            old.Parameters.AddWithValue(generalId);
            old.Parameters.AddWithValue(compositeId);
            await old.ExecuteNonQueryAsync(ct);
        }
        await using (var wear = new NpgsqlCommand(
                         "UPDATE player_equipment_composites SET owner_general_id=$3,updated_at=now() WHERE player_id=$1 AND id=$2", conn, tx))
        {
            wear.Parameters.AddWithValue(playerId);
            wear.Parameters.AddWithValue(compositeId);
            wear.Parameters.AddWithValue(generalId);
            if (await wear.ExecuteNonQueryAsync(ct) != 1)
                throw new GameException("COMPOSITE_STATE_CHANGED", "Suit/Proset đã thay đổi.", 409);
        }

        await tx.CommitAsync(ct);
        return await GetAsync(db, content, playerId, ct);
    }

    public static async Task<EquipmentCompositeInventoryView> UnequipAsync(
        GameDb db,
        CanonicalContent content,
        long playerId,
        long compositeId,
        CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var row = await ReadCompositeAsync(conn, tx, playerId, compositeId, true, ct)
                  ?? throw new GameException("COMPOSITE_NOT_FOUND", "Không tìm thấy Suit/Proset.", 404);
        if (row.OwnerGeneralId is not null)
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE player_equipment_composites SET owner_general_id=NULL,updated_at=now() WHERE player_id=$1 AND id=$2", conn, tx);
            cmd.Parameters.AddWithValue(playerId);
            cmd.Parameters.AddWithValue(compositeId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(db, content, playerId, ct);
    }
}
