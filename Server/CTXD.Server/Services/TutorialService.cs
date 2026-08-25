using System.Text.Json;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class TutorialService(CanonicalContent content, ExperienceService exp)
{
    public async Task<bool> TryCompleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId,
        string eventKind, int[] args, CancellationToken ct)
    {
        int taskId;
        await using (var cmd = new NpgsqlCommand("SELECT current_task_id FROM players WHERE id=$1 FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            taskId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        if (!content.Tasks.TryGetValue(taskId, out var task) ||
            !await MatchesAsync(conn, tx, playerId, task.Target, eventKind, args, ct))
            return false;

        foreach (var reward in task.Reward)
            await ApplyRewardAsync(conn, tx, playerId, reward, ct);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE players SET current_task_id=$2,updated_at=now() WHERE id=$1", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            cmd.Parameters.AddWithValue(task.NextTaskId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return true;
    }

    async Task<bool> MatchesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId,
        TargetDefinition target, string eventKind, int[] args, CancellationToken ct)
    {
        if (target.Kind.Equals("and", StringComparison.OrdinalIgnoreCase) ||
            target.Kind.Equals("or", StringComparison.OrdinalIgnoreCase))
        {
            var raw = target.Raw ?? "";
            var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length <= 1) return false;
            var isAnd = target.Kind.Equals("and", StringComparison.OrdinalIgnoreCase);
            var any = false;
            foreach (var part in parts.Skip(1))
            {
                any = true;
                var sub = ParseLegacyTarget(part);
                var matched = await MatchesAsync(conn, tx, playerId, sub, eventKind, args, ct);
                if (isAnd && !matched) return false;
                if (!isAnd && matched) return true;
            }
            return isAnd && any;
        }

        if (target.Kind.Equals("equip", StringComparison.OrdinalIgnoreCase) ||
            target.Kind.Equals("equip_on", StringComparison.OrdinalIgnoreCase))
            return await MatchesEquipmentStateAsync(conn, tx, playerId, target, target.Kind.Equals("equip_on", StringComparison.OrdinalIgnoreCase), ct);

        return MatchesEvent(target, eventKind, args);
    }

    static TargetDefinition ParseLegacyTarget(string raw)
    {
        var p = raw.Split(',', StringSplitOptions.TrimEntries);
        if (p.Length == 0) return new TargetDefinition { Raw = raw };
        var args = new List<JsonElement>();
        foreach (var x in p.Skip(1))
        {
            if (int.TryParse(x, out var i))
                args.Add(JsonSerializer.SerializeToElement(i));
            else
                args.Add(JsonSerializer.SerializeToElement(x));
        }
        return new TargetDefinition { Kind = p[0], Args = args.ToArray(), Raw = raw };
    }

    static bool MatchesEvent(TargetDefinition t, string kind, int[] args)
    {
        if (!string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase)) return false;
        var expected = t.Args.Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : int.MinValue).ToArray();
        if (expected.Length == 0) return true;
        if (args.Length < expected.Length) return false;
        if (string.Equals(kind, "building_output", StringComparison.OrdinalIgnoreCase) && expected.Length >= 2)
            return args[0] == expected[0] && args[1] >= expected[1];
        for (var i = 0; i < expected.Length; i++)
            if (expected[i] != int.MinValue && expected[i] != args[i]) return false;
        return true;
    }

    static async Task<bool> MatchesEquipmentStateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId,
        TargetDefinition target, bool worn, CancellationToken ct)
    {
        // Legacy TaskRequestEquip(type, quality, degree, num) calls:
        // getNumByLvOrQuality(playerId, type, degree, quality, 1).
        // StoreHouse.xml condition:
        // TYPE=1 AND GOODS_TYPE=type AND ((LV>=degree AND QUALITY=quality) OR QUALITY>quality).
        int Arg(int i) => i < target.Args.Length && target.Args[i].ValueKind == JsonValueKind.Number ? target.Args[i].GetInt32() : 0;
        var goodsType = Arg(0);
        var quality = Arg(1);
        var degree = Arg(2);
        var required = Math.Max(1, Arg(3));
        if (goodsType <= 0 || quality <= 0) return false;

        var ownerFilter = worn ? " AND owner_general_id IS NOT NULL" : "";
        await using var cmd = new NpgsqlCommand($@"SELECT count(*) FROM player_equipment
WHERE player_id=$1 AND goods_type=$2{ownerFilter}
  AND ((level >= $3 AND quality = $4) OR quality > $4)", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(goodsType);
        cmd.Parameters.AddWithValue(degree);
        cmd.Parameters.AddWithValue(quality);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return count >= required;
    }

    async Task ApplyRewardAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, RewardDefinition r, CancellationToken ct)
    {
        int I(int i) => i < r.Args.Length && r.Args[i].ValueKind == JsonValueKind.Number ? r.Args[i].GetInt32() : 0;
        switch (r.Kind)
        {
            case "copper": await AddResource(conn, tx, playerId, "copper", I(0), ct); break;
            case "lumber": await AddResource(conn, tx, playerId, "wood", I(0), ct); break;
            case "food": await AddResource(conn, tx, playerId, "food", I(0), ct); break;
            case "iron": await AddResource(conn, tx, playerId, "iron", I(0), ct); break;
            case "ChiefExp": await exp.AddAsync(conn, tx, playerId, I(0), ct); break;
            case "new_building":
                await using (var cmd = new NpgsqlCommand("INSERT INTO player_buildings(player_id,building_id,level) VALUES($1,$2,1) ON CONFLICT DO NOTHING", conn, tx))
                { cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(I(0)); await cmd.ExecuteNonQueryAsync(ct); }
                break;
            case "functionId":
                await using (var cmd = new NpgsqlCommand("INSERT INTO player_functions(player_id,function_id) VALUES($1,$2) ON CONFLICT DO NOTHING", conn, tx))
                { cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(I(0)); await cmd.ExecuteNonQueryAsync(ct); }
                break;
            case "new_construction":
                await using (var cmd = new NpgsqlCommand("UPDATE players SET construction_slots=construction_slots+1 WHERE id=$1", conn, tx))
                { cmd.Parameters.AddWithValue(playerId); await cmd.ExecuteNonQueryAsync(ct); }
                break;
            case "free_Construction":
                await using (var cmd = new NpgsqlCommand("UPDATE players SET free_construction_num=free_construction_num+$2 WHERE id=$1", conn, tx))
                { cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(I(0)); await cmd.ExecuteNonQueryAsync(ct); }
                break;
            case "construction_complete":
                await using (var cmd = new NpgsqlCommand("UPDATE player_buildings SET upgrade_complete_at=now() WHERE player_id=$1 AND state=1", conn, tx))
                { cmd.Parameters.AddWithValue(playerId); await cmd.ExecuteNonQueryAsync(ct); }
                break;
            case "tavern_lock_on":
            {
                var generalId = I(0);
                await using (var ensure = new NpgsqlCommand("INSERT INTO player_tavern(player_id) VALUES($1) ON CONFLICT DO NOTHING", conn, tx))
                { ensure.Parameters.AddWithValue(playerId); await ensure.ExecuteNonQueryAsync(ct); }
                await using (var cmd = new NpgsqlCommand(@"UPDATE player_tavern
SET locked_general_ids = CASE
  WHEN locked_general_ids='' THEN $2::text
  WHEN position(','||$2::text||',' in ','||locked_general_ids||',')>0 THEN locked_general_ids
  ELSE locked_general_ids||','||$2::text END, updated_at=now() WHERE player_id=$1", conn, tx))
                { cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(generalId); await cmd.ExecuteNonQueryAsync(ct); }
                await using (var offer = new NpgsqlCommand("UPDATE player_tavern_offers SET locked=TRUE WHERE player_id=$1 AND general_id=$2 AND bought=FALSE", conn, tx))
                { offer.Parameters.AddWithValue(playerId); offer.Parameters.AddWithValue(generalId); await offer.ExecuteNonQueryAsync(ct); }
                break;
            }
            case "tavern_lock_off":
                await using (var unlock = new NpgsqlCommand("UPDATE player_tavern SET locked_general_ids='',updated_at=now() WHERE player_id=$1", conn, tx))
                { unlock.Parameters.AddWithValue(playerId); await unlock.ExecuteNonQueryAsync(ct); }
                await using (var offers = new NpgsqlCommand("UPDATE player_tavern_offers SET locked=FALSE WHERE player_id=$1", conn, tx))
                { offers.Parameters.AddWithValue(playerId); await offers.ExecuteNonQueryAsync(ct); }
                break;
            case "refresh_store_equip":
                // Legacy TaskRewardRefreshStoreEquip invokes StoreService.refreshItem(playerId, 1, false).
                // The tutorial service cannot call EquipmentStoreService without creating a circular dependency,
                // so it leaves an explicit one-shot refresh marker that the store consumes on the next read.
                await EnsureStoreAsync(conn, tx, playerId, ct);
                await using (var refresh = new NpgsqlCommand("UPDATE player_store SET pending_refresh_style1=TRUE,updated_at=now() WHERE player_id=$1", conn, tx))
                { refresh.Parameters.AddWithValue(playerId); await refresh.ExecuteNonQueryAsync(ct); }
                break;
            case "refresh_store":
                // Verified legacy TaskRewardRefreshStore only pushes the store UI; it does NOT reroll items.
                // No persistent state mutation is required in the new server.
                break;
            case "store_lock_on":
            {
                // Verified legacy TaskRewardStoreLockOn -> StoreService.addLockId(playerId, equipmentId).
                // addLockId only records the equipment id; it does not lock an already-rendered offer and
                // does not perform an immediate refresh. The next store refresh materializes this forced item.
                var equipmentId = I(0);
                await EnsureStoreAsync(conn, tx, playerId, ct);
                await using var cmd = new NpgsqlCommand(@"UPDATE player_store
SET locked_equipment_ids = CASE
  WHEN locked_equipment_ids='' THEN $2::text
  WHEN position(','||$2::text||',' in ','||locked_equipment_ids||',')>0 THEN locked_equipment_ids
  ELSE locked_equipment_ids||','||$2::text END, updated_at=now()
WHERE player_id=$1", conn, tx);
                cmd.Parameters.AddWithValue(playerId);
                cmd.Parameters.AddWithValue(equipmentId);
                await cmd.ExecuteNonQueryAsync(ct);
                break;
            }
            case "store_lock_off":
                // Verified legacy TaskRewardStoreLockOff only clears PlayerStore.lockEquipId.
                // Existing PlayerItemRefresh.locked flags are independent and must not be mass-unlocked here.
                await EnsureStoreAsync(conn, tx, playerId, ct);
                await using (var clear = new NpgsqlCommand("UPDATE player_store SET locked_equipment_ids='',updated_at=now() WHERE player_id=$1", conn, tx))
                { clear.Parameters.AddWithValue(playerId); await clear.ExecuteNonQueryAsync(ct); }
                break;
            case "auto_construction_stop":
                break; // Auto-upgrade is not active until that legacy feature is ported, therefore already stopped.
        }
    }

    static async Task EnsureStoreAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("INSERT INTO player_store(player_id) VALUES($1) ON CONFLICT DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task AddResource(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, string column, long amount, CancellationToken ct)
    {
        if (column is not ("copper" or "wood" or "food" or "iron")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var cmd = new NpgsqlCommand($"UPDATE player_resources SET {column}={column}+$2 WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(amount);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
