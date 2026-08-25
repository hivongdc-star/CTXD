using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class BuildingService(
    GameDb db,
    CanonicalContent content,
    LegacyFormulaService formula,
    ExperienceService exp,
    TutorialService tutorial,
    ResourceProductionService resources)
{
    public async Task<IReadOnlyList<BuildingView>> GetViewsAsync(long playerId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await CompleteDueAsync(conn, tx, playerId, ct);
        var views = await GetViewsAsync(conn, tx, playerId, ct);
        await tx.CommitAsync(ct);
        return views;
    }

    public async Task<IReadOnlyList<BuildingView>> GetViewsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        CancellationToken ct)
    {
        var outputs = await resources.GetPerBuildingBaseOutputAsync(conn, tx, playerId, ct);
        var list = new List<BuildingView>();
        await using var cmd = new NpgsqlCommand(
            "SELECT building_id,level,state,upgrade_complete_at FROM player_buildings WHERE player_id=$1 ORDER BY building_id",
            conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            var level = r.GetInt32(1);
            var state = r.GetInt16(2);
            var at = r.IsDBNull(3) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(3);
            if (!content.Buildings.TryGetValue(id, out var b)) continue;
            var target = level + 1;
            list.Add(new BuildingView(
                id,
                b.Name,
                level,
                state,
                at,
                b.OutputType,
                outputs.GetValueOrDefault(id),
                formula.CopperCost(b, target),
                formula.WoodCost(b, target),
                formula.UpgradeDurationMs(b, target)));
        }
        return list;
    }

    public async Task<UpgradeResponse> UpgradeAsync(long playerId, int buildingId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await CompleteDueAsync(conn, tx, playerId, ct);
        var res = await resources.AccrueAndGetAsync(playerId, ct, conn, tx);

        int playerLv;
        await using (var p = new NpgsqlCommand("SELECT level FROM players WHERE id=$1 FOR UPDATE", conn, tx))
        {
            p.Parameters.AddWithValue(playerId);
            var raw = await p.ExecuteScalarAsync(ct);
            if (raw is null) throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
            playerLv = Convert.ToInt32(raw);
        }

        int level;
        short state;
        DateTimeOffset? oldComplete;
        await using (var bq = new NpgsqlCommand(
            "SELECT level,state,upgrade_complete_at FROM player_buildings WHERE player_id=$1 AND building_id=$2 FOR UPDATE",
            conn, tx))
        {
            bq.Parameters.AddWithValue(playerId);
            bq.Parameters.AddWithValue(buildingId);
            await using var r = await bq.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GameException("BUILDING_LOCKED", "Công trình chưa mở.");
            level = r.GetInt32(0);
            state = r.GetInt16(1);
            oldComplete = r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2);
        }

        if (level >= playerLv)
            throw new GameException("BUILDING_PLAYER_LEVEL", "Cấp công trình không thể bằng hoặc vượt cấp nhân vật.");
        if (state != 0 || oldComplete is not null)
            throw new GameException("BUILDING_BUSY", "Công trình đang nâng cấp.");
        if (!content.Buildings.TryGetValue(buildingId, out var def))
            throw new GameException("BUILDING_UNKNOWN", "Không có dữ liệu công trình.", 500);

        var target = level + 1;
        var copper = formula.CopperCost(def, target);
        var wood = formula.WoodCost(def, target);
        var duration = formula.UpgradeDurationMs(def, target);
        if (res.Copper < copper) throw new GameException("COPPER_NOT_ENOUGH", "Không đủ bạc.");
        if (res.Wood < wood) throw new GameException("WOOD_NOT_ENOUGH", "Không đủ gỗ.");

        await using (var busy = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM player_buildings WHERE player_id=$1 AND state=1),construction_slots FROM players WHERE id=$1 FOR UPDATE", conn, tx))
        {
            busy.Parameters.AddWithValue(playerId);
            await using var br = await busy.ExecuteReaderAsync(ct);
            if (!await br.ReadAsync(ct)) throw new GameException("PLAYER_NOT_FOUND","Không tìm thấy nhân vật.",404);
            if (br.GetInt64(0) >= br.GetInt32(1))
                throw new GameException("BUILDING_WORK_BUSY", "Đội xây dựng đang bận.");
        }

        var complete = DateTimeOffset.UtcNow.AddMilliseconds(duration);
        await using (var rcmd = new NpgsqlCommand(
            "UPDATE player_resources SET copper=copper-$2,wood=wood-$3 WHERE player_id=$1", conn, tx))
        {
            rcmd.Parameters.AddWithValue(playerId);
            rcmd.Parameters.AddWithValue(copper);
            rcmd.Parameters.AddWithValue(wood);
            await rcmd.ExecuteNonQueryAsync(ct);
        }
        await using (var bcmd = new NpgsqlCommand(
            "UPDATE player_buildings SET state=1,upgrade_complete_at=$3 WHERE player_id=$1 AND building_id=$2", conn, tx))
        {
            bcmd.Parameters.AddWithValue(playerId);
            bcmd.Parameters.AddWithValue(buildingId);
            bcmd.Parameters.AddWithValue(complete);
            await bcmd.ExecuteNonQueryAsync(ct);
        }

        var newResources = res with { Copper = res.Copper - copper, Wood = res.Wood - wood };
        var outputs = await resources.GetPerBuildingBaseOutputAsync(conn, tx, playerId, ct);
        var view = new BuildingView(
            buildingId,
            def.Name,
            level,
            1,
            complete,
            def.OutputType,
            outputs.GetValueOrDefault(buildingId),
            formula.CopperCost(def, target),
            formula.WoodCost(def, target),
            duration);

        await tx.CommitAsync(ct);
        return new UpgradeResponse(view, newResources);
    }

    public async Task<int> CompleteDueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        CancellationToken ct)
    {
        var total = 0;
        for (var round = 0; round < 16; round++)
        {
            var due = new List<(int Id, int Level, DateTimeOffset CompleteAt)>();
            await using (var q = new NpgsqlCommand(
                "SELECT building_id,level,upgrade_complete_at FROM player_buildings WHERE player_id=$1 AND state=1 AND upgrade_complete_at<=now() ORDER BY upgrade_complete_at FOR UPDATE",
                conn, tx))
            {
                q.Parameters.AddWithValue(playerId);
                await using var r = await q.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) due.Add((r.GetInt32(0), r.GetInt32(1), r.GetFieldValue<DateTimeOffset>(2)));
            }
            if (due.Count == 0) break;

            foreach (var x in due)
            {
                if (!content.Buildings.TryGetValue(x.Id, out var def)) continue;
                await resources.AccrueAndGetAtAsync(playerId, x.CompleteAt, ct, conn, tx);
                var newLv = x.Level + 1;
                await using (var u = new NpgsqlCommand(
                    "UPDATE player_buildings SET level=$3,state=0,upgrade_complete_at=NULL WHERE player_id=$1 AND building_id=$2 AND state=1",
                    conn, tx))
                {
                    u.Parameters.AddWithValue(playerId);
                    u.Parameters.AddWithValue(x.Id);
                    u.Parameters.AddWithValue(newLv);
                    if (await u.ExecuteNonQueryAsync(ct) == 0) continue;
                }
                total++;
                await exp.AddAsync(conn, tx, playerId, formula.ChiefExp(def, newLv), ct);
                await tutorial.TryCompleteAsync(conn, tx, playerId, "building", [x.Id, newLv], ct);
            }
        }
        return total;
    }
}
