using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

internal sealed class ResourceAdditionService
{
    public async Task<int> GetBuildingOutputContributionAsync(
        NpgsqlConnection c,
        NpgsqlTransaction t,
        long playerId,
        int resourceType,
        int baseOutput,
        CancellationToken ct)
    {
        if(baseOutput<=0)return 0;

        await using var q=new NpgsqlCommand(@"
SELECT addition_mode
FROM player_resource_additions
WHERE player_id=$1 AND resource_type=$2 AND ends_at>now()",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(resourceType);
        var raw=await q.ExecuteScalarAsync(ct);
        if(raw is null or DBNull)return 0;

        var multiplier=LegacyMultiplier(Convert.ToInt32(raw));
        return (int)(baseOutput*(multiplier-1d));
    }

    static double LegacyMultiplier(int additionMode)=>additionMode switch
    {
        // Legacy BuildingService.getId maps recruit/type 5 modes 1/2/3 to chargeitem
        // 49/50/51. Original chargeitem.param is DOUBLE: 1.5 / 2.0 / 3.0.
        // The current canonical int model truncates id 49, so use the verified source values here.
        1=>1.5d,
        2=>2d,
        3=>3d,
        _=>throw new GameException("RESOURCE_ADDITION_MODE_INVALID",$"Legacy resource addition mode {additionMode} is invalid.",500)
    };
}
