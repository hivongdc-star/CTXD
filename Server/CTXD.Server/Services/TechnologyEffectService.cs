using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

/// <summary>
/// Read-only projection of completed legacy technology effects. For combat key 30, the legacy
/// global Arms Weapon flat ATT/DEF/Blood values are folded into the same downstream projection
/// so battle and recruit-cap calculations consume one authoritative stat source.
/// </summary>
public sealed class TechnologyEffectService(GameDb db, CanonicalContent content)
{
    public async Task<double> GetCompletedEffectAsync(
        long playerId,
        int key,
        int parameterIndex,
        CancellationToken ct,
        NpgsqlConnection? existing = null,
        NpgsqlTransaction? tx = null)
    {
        if (parameterIndex is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(parameterIndex));

        var own = existing is null;
        var conn = existing ?? await db.DataSource.OpenConnectionAsync(ct);
        try
        {
            var ids = new List<int>();
            await using (var cmd = new NpgsqlCommand(@"
SELECT technology_id
FROM player_technologies
WHERE player_id=$1 AND key_id=$2 AND status=5", conn, tx))
            {
                cmd.Parameters.AddWithValue(playerId);
                cmd.Parameters.AddWithValue(key);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) ids.Add(r.GetInt32(0));
            }

            double sum = 0;
            foreach (var id in ids)
            {
                if (content.Technologies.TryGetValue(id, out var definition) &&
                    definition.Parameters.Length > parameterIndex)
                    sum += definition.Parameters[parameterIndex];
            }

            if (key == 30 && parameterIndex < 3)
            {
                var weapon = await WeaponService.BattleEffectAsync(conn, tx, content, playerId, ct);
                sum += parameterIndex switch
                {
                    0 => weapon.Attack,
                    1 => weapon.Defense,
                    2 => weapon.Blood,
                    _ => 0
                };
            }
            return sum;
        }
        finally
        {
            if (own) await conn.DisposeAsync();
        }
    }

    public async Task<int> GetCompletedIntEffectAsync(
        long playerId,
        int key,
        int parameterIndex,
        CancellationToken ct,
        NpgsqlConnection? existing = null,
        NpgsqlTransaction? tx = null)
    {
        var value = await GetCompletedEffectAsync(playerId, key, parameterIndex, ct, existing, tx);
        return (int)Math.Truncate(value);
    }
}
