using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class ResourceProductionService(GameDb db, CanonicalContent content, TechnologyEffectService technologyEffects)
{
    static readonly Dictionary<int, int> LimitBuildingByResource = new() { [1] = 16, [2] = 32, [3] = 48, [4] = 64 };

    public Task<ResourceView> AccrueAndGetAsync(
        long playerId,
        CancellationToken ct,
        NpgsqlConnection? existing = null,
        NpgsqlTransaction? tx = null) =>
        AccrueAndGetAtAsync(playerId, DateTimeOffset.UtcNow, ct, existing, tx);

    public async Task<ResourceView> AccrueAndGetAtAsync(
        long playerId,
        DateTimeOffset effectiveNow,
        CancellationToken ct,
        NpgsqlConnection? existing = null,
        NpgsqlTransaction? tx = null)
    {
        var own = existing is null;
        var conn = existing ?? await db.DataSource.OpenConnectionAsync(ct);
        NpgsqlTransaction? ownTx = null;
        if (tx is null)
        {
            ownTx = await conn.BeginTransactionAsync(ct);
            tx = ownTx;
        }
        try
        {
            var buildings = await ReadBuildingsAsync(conn, tx, playerId, ct);
            long copper, wood, food, iron;
            DateTimeOffset updateTime;
            await using (var cmd = new NpgsqlCommand(
                "SELECT copper,wood,food,iron,update_time FROM player_resources WHERE player_id=$1 FOR UPDATE", conn, tx))
            {
                cmd.Parameters.AddWithValue(playerId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) throw new GameException("RESOURCE_MISSING", "Thiếu player resource.", 500);
                copper = r.GetInt64(0);
                wood = r.GetInt64(1);
                food = r.GetInt64(2);
                iron = r.GetInt64(3);
                updateTime = r.GetFieldValue<DateTimeOffset>(4);
            }

            var elapsedMs = Math.Max(0L, (long)(effectiveNow - updateTime).TotalMilliseconds);
            var ticks10 = elapsedMs / 10_000L;
            var elapsedSec = ticks10 * 10L; // exact 10-second quantization from legacy ResourceService.output

            // Legacy BuildingOutputCache only applies production technology to type 3 (food):
            // tech output = base building output * TechEffect(key=6) / 100, truncated to int.
            // Resource storage buildings (outputType=4) apply TechEffect(key=20) as
            // base capacity * (1 + percent / 100), including the legacy 10,000 fallback.
            var foodTechPercent = await technologyEffects.GetCompletedIntEffectAsync(playerId, 6, 0, ct, conn, tx);
            var storageTechPercent = await technologyEffects.GetCompletedIntEffectAsync(playerId, 20, 0, ct, conn, tx);
            var rates = Enumerable.Range(1, 4).ToDictionary(t => t, t => GetTotalOutput(buildings, t, foodTechPercent));
            var max = Enumerable.Range(1, 4).ToDictionary(t => t, t => GetMax(buildings, t, storageTechPercent));

            if (elapsedSec >= 60)
            {
                copper = Math.Min(max[1], copper + (long)(rates[1] / 3600d * elapsedSec));
                wood = Math.Min(max[2], wood + (long)(rates[2] / 3600d * elapsedSec));
                food = Math.Min(max[3], food + (long)(rates[3] / 3600d * elapsedSec));
                iron = Math.Min(max[4], iron + (long)(rates[4] / 3600d * elapsedSec));
                updateTime = updateTime.AddSeconds(elapsedSec);
                await using var u = new NpgsqlCommand(
                    "UPDATE player_resources SET copper=$2,wood=$3,food=$4,iron=$5,update_time=$6 WHERE player_id=$1", conn, tx);
                u.Parameters.AddWithValue(playerId);
                u.Parameters.AddWithValue(copper);
                u.Parameters.AddWithValue(wood);
                u.Parameters.AddWithValue(food);
                u.Parameters.AddWithValue(iron);
                u.Parameters.AddWithValue(updateTime);
                await u.ExecuteNonQueryAsync(ct);
            }

            if (ownTx is not null) await ownTx.CommitAsync(ct);
            return new ResourceView(copper, wood, food, iron, updateTime,
                rates[1], rates[2], rates[3], rates[4], max[1], max[2], max[3], max[4]);
        }
        catch
        {
            if (ownTx is not null) await ownTx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (ownTx is not null) await ownTx.DisposeAsync();
            if (own) await conn.DisposeAsync();
        }
    }

    public async Task<IReadOnlyDictionary<int, int>> GetPerBuildingBaseOutputAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        CancellationToken ct)
    {
        var buildings = await ReadBuildingsAsync(conn, tx, playerId, ct);
        var result = new Dictionary<int, int>();
        foreach (var pb in buildings.Values)
        {
            if (content.Buildings.TryGetValue(pb.Id, out var def))
                result[pb.Id] = GetBuildingOutput(buildings, def, pb.Level, new HashSet<int>());
        }
        return result;
    }

    sealed record PB(int Id, int Level);

    async Task<Dictionary<int, PB>> ReadBuildingsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        CancellationToken ct)
    {
        var d = new Dictionary<int, PB>();
        await using var cmd = new NpgsqlCommand("SELECT building_id,level FROM player_buildings WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var b = new PB(r.GetInt32(0), r.GetInt32(1));
            d[b.Id] = b;
        }
        return d;
    }

    int GetTotalOutput(Dictionary<int, PB> pbs, int resourceType, int foodTechPercent)
    {
        var sum = 0;
        foreach (var pb in pbs.Values)
        {
            if (!content.Buildings.TryGetValue(pb.Id, out var b) || b.AreaType != resourceType || b.OutputType == 4) continue;
            sum += GetBuildingOutput(pbs, b, pb.Level, new HashSet<int>());
        }
        // Exact legacy tech mapping for resource output: type 3 -> key 6; types 1/2/4 -> no production tech.
        if (resourceType == 3 && foodTechPercent != 0)
            sum += (int)(sum * (foodTechPercent / 100d));
        // Officer/player-resource-addition bonuses remain deferred until those legacy systems are ported.
        return sum;
    }

    int GetBuildingOutput(Dictionary<int, PB> pbs, BuildingDefinition b, int level, HashSet<int> path)
    {
        if (!path.Add(b.Id)) return 0; // protect malformed/cyclic legacy data without inventing output.
        try
        {
            if (b.OutputType is 1 or 4 or 5)
                return (int)(b.OutputExponent * content.Serial(b.OutputSeriesId, level));
            if (b.OutputType is 2 or 3)
            {
                var related = 0;
                foreach (var id in b.OutputRelatedBuildings)
                {
                    if (pbs.TryGetValue(id, out var pb) && content.Buildings.TryGetValue(id, out var rb))
                        related += GetBuildingOutput(pbs, rb, pb.Level, path);
                }
                return (int)(b.OutputExponent * content.Serial(b.OutputSeriesId, level) + b.OutputRelatedFactor * related);
            }
            return 0;
        }
        finally
        {
            path.Remove(b.Id);
        }
    }

    long GetMax(Dictionary<int, PB> pbs, int resourceType, int storageTechPercent)
    {
        var id = LimitBuildingByResource[resourceType];
        long baseCapacity;
        if (!pbs.TryGetValue(id, out var pb) || !content.Buildings.TryGetValue(id, out var b))
            baseCapacity = 10_000; // exact legacy calcOutput fallback when the warehouse building is absent.
        else
            baseCapacity = GetBuildingOutput(pbs, b, pb.Level, new HashSet<int>());

        // Exact legacy BuildingOutputCache.getBuildingOutput() rule for outputType=4.
        return (long)(baseCapacity * (1d + storageTechPercent / 100d));
    }
}
