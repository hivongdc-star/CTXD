using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record LevelRankEntry(long PlayerId,string PlayerName,int PlayerLv);
public sealed record LevelRankView(LevelRankEntry[] RankList);

public sealed class RankService(GameDb db)
{
    public async Task<LevelRankView> GetAsync(int rankId,CancellationToken ct)
    {
        // Legacy RankService.getRankList only accepts rankId=1 and initializes it from:
        // SELECT * FROM PLAYER ORDER BY PLAYER_LV DESC LIMIT 0,200
        if(rankId!=1)throw new GameException("RANK_TYPE_INVALID","Legacy public ranking only supports rank type 1.",404);

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        var items=new List<LevelRankEntry>(200);
        await using var q=new NpgsqlCommand("SELECT id,COALESCE(display_name,''),level FROM players ORDER BY level DESC LIMIT 200",c);
        await using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))items.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2)));
        return new(items.ToArray());
    }
}

public static class RankEndpoints
{
    public static IEndpointRouteBuilder MapRankEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rank/{rankId:int}",async(int rankId,HttpRequest request,AuthService auth,GameDb db,CancellationToken ct)=>
        {
            _=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new RankService(db).GetAsync(rankId,ct));
        });
        return app;
    }

    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}
