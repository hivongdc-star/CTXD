using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

/// <summary>
/// Port of the legacy TechService state machine. Legacy statuses are preserved:
/// 0 locked/not dropped, 1 fully injected/ready, 2 available/no injection,
/// 3 partially injected, 4 researching, 5 completed.
/// </summary>
public sealed class TechnologyService(
    GameDb db,
    CanonicalContent content,
    TutorialService tutorial,
    ResourceProductionService resources,
    TechnologyEffectService technologyEffects)
{
    const int TechFunction = 19;
    const int PageSize = 8;

    public async Task<TechnologyListResponse> GetAsync(long playerId, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await RequireOpenAsync(conn, tx, playerId, ct);
        await CompleteDueForPlayerAsync(conn, tx, playerId, ct);

        var total = await CountAsync(conn, tx, playerId, ct);
        var offset = (page - 1) * PageSize;
        var rows = await ReadPageAsync(conn, tx, playerId, offset, PageSize, ct);
        var views = rows.Select(ToView).ToArray();
        var totalPage = total == 0 ? 0 : (total + PageSize - 1) / PageSize;

        // Legacy getTechInfo clears visual "new" flags once the list is read.
        if (rows.Any(x => x.IsNew || x.FinishNew))
        {
            await using var clear = new NpgsqlCommand(@"
UPDATE player_technologies SET is_new=FALSE,finish_new=FALSE,updated_at=now()
WHERE player_id=$1 AND (is_new=TRUE OR finish_new=TRUE)", conn, tx);
            clear.Parameters.AddWithValue(playerId);
            await clear.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new TechnologyListResponse(page, totalPage, views);
    }

    public async Task<TechnologyInjectResponse> InjectAsync(long playerId, int technologyId, CancellationToken ct)
    {
        if (!content.Technologies.TryGetValue(technologyId, out var def))
            throw new GameException("TECH_NOT_FOUND", "Không có khoa kỹ này.", 404);

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await RequireOpenAsync(conn, tx, playerId, ct);
        await CompleteDueForPlayerAsync(conn, tx, playerId, ct);
        await resources.AccrueAndGetAsync(playerId, ct, conn, tx);

        var row = await ReadForUpdateAsync(conn, tx, playerId, technologyId, ct)
                  ?? throw new GameException("TECH_NOT_DROPPED", "Khoa kỹ này chưa được mở.");
        if (row.Status is not (2 or 3))
            throw new GameException("TECH_NO_INJECT", "Khoa kỹ này hiện không thể chú tư.");

        var costs = ParseCosts(def.Resource);
        await ConsumeResourcesAsync(conn, tx, playerId, costs, ct);

        var old = row.InjectedCount;
        var total = Math.Max(1, def.ResourceTimes);
        if (old >= total)
            throw new GameException("TECH_INJECT_COMPLETE", "Khoa kỹ đã chú tư đủ.");
        var next = old + 1;
        var nextStatus = next == total ? 1 : (old == 0 ? 3 : row.Status);

        await using (var update = new NpgsqlCommand(@"
UPDATE player_technologies
SET injected_count=$3,status=$4,updated_at=now()
WHERE player_id=$1 AND technology_id=$2", conn, tx))
        {
            update.Parameters.AddWithValue(playerId);
            update.Parameters.AddWithValue(technologyId);
            update.Parameters.AddWithValue(next);
            update.Parameters.AddWithValue(nextStatus);
            await update.ExecuteNonQueryAsync(ct);
        }

        // Legacy TaskMessageTechInject carries techId; TaskRequestTechInject verifies current num.
        // Passing current num as the second argument lets the canonical tutorial target tech_inject,id,times
        // retain the exact observable condition without creating a separate legacy task counter table.
        await tutorial.TryCompleteAsync(conn, tx, playerId, "tech_inject", [technologyId, next], ct);

        var updated = row with { InjectedCount = next, Status = nextStatus, UpdatedAt = DateTimeOffset.UtcNow };
        var res = await resources.AccrueAndGetAsync(playerId, ct, conn, tx);
        await tx.CommitAsync(ct);
        return new TechnologyInjectResponse(ToView(updated), res);
    }

    public async Task<TechnologyResearchResponse> ResearchAsync(long playerId, int technologyId, CancellationToken ct)
    {
        if (!content.Technologies.TryGetValue(technologyId, out var def))
            throw new GameException("TECH_NOT_FOUND", "Không có khoa kỹ này.", 404);

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await RequireOpenAsync(conn, tx, playerId, ct);
        await CompleteDueForPlayerAsync(conn, tx, playerId, ct);

        var row = await ReadForUpdateAsync(conn, tx, playerId, technologyId, ct)
                  ?? throw new GameException("TECH_NOT_DROPPED", "Khoa kỹ này chưa được mở.");
        if (row.Status != 1)
            throw new GameException("TECH_CAN_NOT_RESEARCH", "Khoa kỹ chưa đủ điều kiện nghiên cứu.");

        var completeAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(0, def.ResearchTime));
        await using (var update = new NpgsqlCommand(@"
UPDATE player_technologies
SET status=4,research_complete_at=$3,updated_at=now()
WHERE player_id=$1 AND technology_id=$2", conn, tx))
        {
            update.Parameters.AddWithValue(playerId);
            update.Parameters.AddWithValue(technologyId);
            update.Parameters.AddWithValue(completeAt);
            await update.ExecuteNonQueryAsync(ct);
        }

        // Legacy research sends both generic research and tech-specific research-begin messages.
        await tutorial.TryCompleteAsync(conn, tx, playerId, "tech_research", [], ct);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "tech_research_begin", [technologyId], ct);

        var updated = row with { Status = 4, ResearchCompleteAt = completeAt, UpdatedAt = DateTimeOffset.UtcNow };
        await tx.CommitAsync(ct);
        return new TechnologyResearchResponse(ToView(updated));
    }

    /// <summary>
    /// Equivalent to legacy TechService.dropTech(playerId, techId). Battle/progression code should call this
    /// when the legacy reward says a technology has dropped.
    /// </summary>
    public async Task<TechnologyView> DropTechAsync(long playerId, int technologyId, CancellationToken ct)
    {
        if (!content.Technologies.TryGetValue(technologyId, out var def) || def.DropIndex <= 0)
            throw new GameException("TECH_DROP_INVALID", "Khoa kỹ không thuộc chuỗi mở khóa.");

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureInitialDropSlotsAsync(conn, tx, playerId, ct);

        var existing = await ReadForUpdateAsync(conn, tx, playerId, technologyId, ct);
        if (existing is null)
        {
            await InsertTechAsync(conn, tx, playerId, def, status: 2, isNew: true, ct);
        }
        else if (existing.Status == 0)
        {
            await using var activate = new NpgsqlCommand(@"
UPDATE player_technologies SET status=2,is_new=TRUE,updated_at=now()
WHERE player_id=$1 AND technology_id=$2 AND status=0", conn, tx);
            activate.Parameters.AddWithValue(playerId);
            activate.Parameters.AddWithValue(technologyId);
            await activate.ExecuteNonQueryAsync(ct);
        }

        // Legacy appends the next technology according to the count of PlayerTech rows.
        var count = await CountAsync(conn, tx, playerId, ct);
        var next = DropOrdered().ElementAtOrDefault(count);
        if (next is not null && await ReadForUpdateAsync(conn, tx, playerId, next.Id, ct) is null)
            await InsertTechAsync(conn, tx, playerId, next, status: 0, isNew: false, ct);

        var result = await ReadForUpdateAsync(conn, tx, playerId, technologyId, ct)
                     ?? throw new GameException("TECH_DROP_FAILED", "Không thể mở khoa kỹ.", 500);
        await tx.CommitAsync(ct);
        return ToView(result);
    }

    public async Task<IReadOnlyList<long>> CompleteDueAsync(CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        var ids = new List<long>();
        await using (var cmd = new NpgsqlCommand(@"
SELECT DISTINCT player_id FROM player_technologies
WHERE status=4 AND research_complete_at IS NOT NULL AND research_complete_at <= now()", conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) ids.Add(r.GetInt64(0));

        var changed = new List<long>();
        foreach (var playerId in ids)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (await CompleteDueForPlayerAsync(conn, tx, playerId, ct) > 0) changed.Add(playerId);
            await tx.CommitAsync(ct);
        }
        return changed;
    }

    public Task<double> GetCompletedEffectAsync(long playerId, int key, int parameterIndex, CancellationToken ct) =>
        technologyEffects.GetCompletedEffectAsync(playerId, key, parameterIndex, ct);

    async Task<int> CompleteDueForPlayerAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        var due = new List<TechRow>();
        await using (var cmd = new NpgsqlCommand(@"
SELECT technology_id,key_id,injected_count,status,research_complete_at,is_new,finish_new,created_at,updated_at
FROM player_technologies
WHERE player_id=$1 AND status=4 AND research_complete_at IS NOT NULL AND research_complete_at <= now()
FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) due.Add(ReadRow(r));
        }
        foreach (var row in due)
        {
            // Settle passive resources up to the technology's actual completion boundary while the
            // old technology effect is still active. Otherwise a lazy resource tick could apply a
            // newly completed production/capacity technology retroactively to the whole elapsed interval.
            if (row.ResearchCompleteAt is { } completedAt)
                await resources.AccrueAndGetAtAsync(playerId, completedAt, ct, conn, tx);

            await using (var update = new NpgsqlCommand(@"
UPDATE player_technologies
SET status=5,research_complete_at=$3,updated_at=now()
WHERE player_id=$1 AND technology_id=$2 AND status=4", conn, tx))
            {
                update.Parameters.AddWithValue(playerId);
                update.Parameters.AddWithValue(row.Id);
                update.Parameters.AddWithValue(row.ResearchCompleteAt ?? DateTimeOffset.UtcNow);
                await update.ExecuteNonQueryAsync(ct);
            }
            await tutorial.TryCompleteAsync(conn, tx, playerId, "tech_research_done", [row.Id], ct);
        }
        return due.Count;
    }

    async Task EnsureInitialDropSlotsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        // Legacy openTechFunction creates the first two drop-index technologies as status 0.
        var count = await CountAsync(conn, tx, playerId, ct);
        if (count >= 2) return;
        foreach (var def in DropOrdered().Take(2))
        {
            if (await ReadForUpdateAsync(conn, tx, playerId, def.Id, ct) is null)
                await InsertTechAsync(conn, tx, playerId, def, status: 0, isNew: false, ct);
        }
    }

    IEnumerable<TechnologyDefinition> DropOrdered() =>
        content.Technologies.Values.Where(x => x.DropIndex > 0).OrderBy(x => x.DropIndex).ThenBy(x => x.Id);

    static IReadOnlyList<TechnologyResourceCost> ParseCosts(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var result = new List<TechnologyResourceCost>();
        foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = entry.Split(',', StringSplitOptions.TrimEntries);
            if (p.Length != 2 || !long.TryParse(p[1], out var value) || value < 0) continue;
            var kind = p[0].ToLowerInvariant() switch
            {
                "copper" => "copper",
                "lumber" => "wood",
                "wood" => "wood",
                "food" => "food",
                "iron" => "iron",
                _ => ""
            };
            if (kind.Length > 0) result.Add(new TechnologyResourceCost(kind, value));
        }
        return result;
    }

    static async Task ConsumeResourcesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId,
        IReadOnlyList<TechnologyResourceCost> costs, CancellationToken ct)
    {
        if (costs.Count == 0) return;
        long copper, wood, food, iron;
        await using (var read = new NpgsqlCommand(
            "SELECT copper,wood,food,iron FROM player_resources WHERE player_id=$1 FOR UPDATE", conn, tx))
        {
            read.Parameters.AddWithValue(playerId);
            await using var r = await read.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GameException("RESOURCE_MISSING", "Thiếu player resource.", 500);
            copper=r.GetInt64(0); wood=r.GetInt64(1); food=r.GetInt64(2); iron=r.GetInt64(3);
        }
        long Need(string type) => costs.Where(x=>x.Type==type).Sum(x=>x.Value);
        var nc=Need("copper"); var nw=Need("wood"); var nf=Need("food"); var ni=Need("iron");
        if (copper < nc) throw new GameException("COPPER_NOT_ENOUGH", "Bạc không đủ.");
        if (wood < nw) throw new GameException("WOOD_NOT_ENOUGH", "Gỗ không đủ.");
        if (food < nf) throw new GameException("FOOD_NOT_ENOUGH", "Lương thực không đủ.");
        if (iron < ni) throw new GameException("IRON_NOT_ENOUGH", "Sắt không đủ.");
        await using var update = new NpgsqlCommand(@"
UPDATE player_resources SET copper=copper-$2,wood=wood-$3,food=food-$4,iron=iron-$5 WHERE player_id=$1", conn, tx);
        update.Parameters.AddWithValue(playerId); update.Parameters.AddWithValue(nc); update.Parameters.AddWithValue(nw);
        update.Parameters.AddWithValue(nf); update.Parameters.AddWithValue(ni);
        await update.ExecuteNonQueryAsync(ct);
    }

    async Task RequireOpenAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2)", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(TechFunction);
        if (!Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)))
            throw new GameException("TECH_LOCKED", "Chức năng Khoa Kỹ chưa mở.", 403);
    }

    async Task InsertTechAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, TechnologyDefinition def,
        int status, bool isNew, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO player_technologies(player_id,technology_id,key_id,injected_count,status,research_complete_at,is_new,finish_new)
VALUES($1,$2,$3,0,$4,NULL,$5,FALSE)
ON CONFLICT(player_id,technology_id) DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(def.Id); cmd.Parameters.AddWithValue(def.Key);
        cmd.Parameters.AddWithValue(status); cmd.Parameters.AddWithValue(isNew);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task<int> CountAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM player_technologies WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    async Task<TechRow?> ReadForUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int technologyId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
SELECT technology_id,key_id,injected_count,status,research_complete_at,is_new,finish_new,created_at,updated_at
FROM player_technologies WHERE player_id=$1 AND technology_id=$2 FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(technologyId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadRow(r) : null;
    }

    static async Task<List<TechRow>> ReadPageAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int offset, int count, CancellationToken ct)
    {
        var list = new List<TechRow>();
        await using var cmd = new NpgsqlCommand(@"
SELECT technology_id,key_id,injected_count,status,research_complete_at,is_new,finish_new,created_at,updated_at
FROM player_technologies WHERE player_id=$1
ORDER BY created_at DESC,technology_id DESC OFFSET $2 LIMIT $3", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(offset); cmd.Parameters.AddWithValue(count);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(ReadRow(r));
        return list;
    }

    static TechRow ReadRow(NpgsqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt16(3),
        r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4),
        r.GetBoolean(5), r.GetBoolean(6), r.GetFieldValue<DateTimeOffset>(7), r.GetFieldValue<DateTimeOffset>(8));

    TechnologyView ToView(TechRow row)
    {
        if (!content.Technologies.TryGetValue(row.Id, out var def))
            throw new GameException("TECH_STATIC_MISSING", $"Thiếu static technology {row.Id}.", 500);
        return new TechnologyView(
            def.Id, def.Key, def.KeyString, def.Name, def.Pic, def.Intro,
            row.Status, row.InjectedCount, Math.Max(1, def.ResourceTimes), row.ResearchCompleteAt,
            checked(Math.Max(0, def.ResearchTime) * 60 * 1000), row.IsNew, row.FinishNew,
            ParseCosts(def.Resource), def.Parameters ?? []);
    }

    sealed record TechRow(
        int Id, int Key, int InjectedCount, int Status, DateTimeOffset? ResearchCompleteAt,
        bool IsNew, bool FinishNew, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
