using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class FarmArrivalService(
    GameDb db,
    CanonicalContent content,
    ResourceProductionService production,
    ExperienceService experience,
    IPlayerItemInventory items,
    DstqActivityService dstq,
    GamePushHub push)
{
    static readonly IReadOnlyDictionary<int,int> FarmCities=new Dictionary<int,int>{{1,254},{2,253},{3,206}};
    readonly FarmService farm=new(db,content,production,experience,items,dstq,push);

    public async Task<int> SettleAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        int force;
        await using(var p=new NpgsqlCommand("SELECT force_id FROM players WHERE id=$1",c,t))
        {
            p.Parameters.AddWithValue(playerId);
            var value=await p.ExecuteScalarAsync(ct);
            if(value is null){await t.CommitAsync(ct);return 0;}
            force=Convert.ToInt32(value);
        }
        if(!FarmCities.TryGetValue(force,out var city)){await t.CommitAsync(ct);return 0;}

        var generals=new List<int>();
        await using(var q=new NpgsqlCommand(@"
SELECT g.general_id
FROM player_generals g
LEFT JOIN player_farms f ON f.player_id=g.player_id AND f.general_id=g.general_id
WHERE g.player_id=$1 AND g.general_type=2 AND g.location_id=$2 AND g.state<=1 AND f.id IS NULL
ORDER BY g.general_id
FOR UPDATE OF g",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(city);
            await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))generals.Add(r.GetInt32(0));
        }
        var changed=0;
        foreach(var generalId in generals)
        {
            await farm.AutoStartOnEnterAsync(c,t,playerId,generalId,force,ct);
            changed++;
        }
        await t.CommitAsync(ct);
        return changed;
    }
}
