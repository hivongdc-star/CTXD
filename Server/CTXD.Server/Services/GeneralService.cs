using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class GeneralService(GameDb db, CanonicalContent content, TechnologyEffectService technologyEffects)
{
    const int CivilType = 1;
    const int MilitaryType = 2;
    const int CivilPositionTechKey = 32;
    const int MilitaryPositionTechKey = 27;

    public async Task<GeneralRosterResponse> GetRosterAsync(long playerId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        int level;
        await using (var p = new NpgsqlCommand("SELECT level FROM players WHERE id=$1", conn))
        {
            p.Parameters.AddWithValue(playerId);
            var v = await p.ExecuteScalarAsync(ct);
            if (v is null) throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
            level = Convert.ToInt32(v);
        }

        var list = new List<GeneralView>();
        await using (var cmd = new NpgsqlCommand(@"
SELECT general_id,general_type,level,exp,leader_bonus,strength_bonus,intel_bonus,politics_bonus,
       forces,location_id,state,morale,auto_state
FROM player_generals WHERE player_id=$1 ORDER BY general_type,general_id", conn))
        {
            cmd.Parameters.AddWithValue(playerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                if (!content.Generals.TryGetValue(id, out var g)) continue;
                list.Add(new GeneralView(
                    id, g.Name, g.Type, g.Pic, g.Quality,
                    r.GetInt32(2), r.GetInt64(3),
                    g.Leader + r.GetInt32(4), g.Strength + r.GetInt32(5),
                    g.Intel + r.GetInt32(6), g.Politics + r.GetInt32(7),
                    g.TroopId, g.TacticId, g.StratagemId,
                    r.GetInt32(8), r.GetInt32(9), r.GetInt16(10), r.GetInt32(11), r.GetInt16(12)));
            }
        }

        var civilMax = await MaxPositionCountAsync(playerId, level, CivilType, ct, conn);
        var militaryMax = await MaxPositionCountAsync(playerId, level, MilitaryType, ct, conn);
        return new GeneralRosterResponse(
            civilMax,
            militaryMax,
            list.Where(x => x.Type == CivilType).ToArray(),
            list.Where(x => x.Type == MilitaryType).ToArray());
    }

    public int BasePositionCount(int playerLevel, int type) =>
        content.GeneralPositions.Count(x => x.Type == type && x.OpenLevel <= playerLevel);

    /// <summary>
    /// Exact legacy rule used by TavernService.getMaxGeneralNum:
    /// civil slots = GeneralPosition(level,type=1) + TechEffect key 32;
    /// military slots = GeneralPosition(level,type=2) + TechEffect key 27.
    /// </summary>
    public async Task<int> MaxPositionCountAsync(
        long playerId,
        int playerLevel,
        int type,
        CancellationToken ct,
        NpgsqlConnection? existing = null,
        NpgsqlTransaction? tx = null)
    {
        var key = type switch
        {
            CivilType => CivilPositionTechKey,
            MilitaryType => MilitaryPositionTechKey,
            _ => 0
        };
        var technologyBonus = key == 0
            ? 0
            : await technologyEffects.GetCompletedIntEffectAsync(playerId, key, 0, ct, existing, tx);
        return BasePositionCount(playerLevel, type) + Math.Max(0, technologyBonus);
    }
}
