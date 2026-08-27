using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public static class MineEndpoints
{
    public static IEndpointRouteBuilder MapMineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/world/mines",async(int? page,int? style,HttpRequest request,AuthService auth,MineService mines,GameDb db,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var mineStyle=style??1;var result=await mines.GetAsync(player,page??0,mineStyle,ct);
            if(mineStyle==1)await QuestEventLedger.RecordCurrentAsync(db,player,"world_mine_iron_visit",0,ct);
            return Results.Ok(result);
        });
        app.MapPost("/api/world/mines/occupy",async(MineOccupyRequest body,HttpRequest request,AuthService auth,MineService mines,GameDb db,CancellationToken ct)=>{var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await mines.OccupyAsync(player,body.MineId,body.GeneralId,ct);if(await IsIronMineAsync(db,body.MineId,ct))await QuestEventLedger.RecordCurrentAsync(db,player,"world_mine_iron_own",0,ct);return Results.Ok(result);});
        app.MapPost("/api/world/mines/rush/{style:int}",async(int style,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>{await mines.RushAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),style,ct);return Results.Ok();});
        app.MapPost("/api/world/mines/abandon/{style:int}",async(int style,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>Results.Ok(await mines.AbandonAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),style,ct)));
        app.MapPost("/api/world/mines/harvest/{style:int}",async(int style,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>Results.Ok(await mines.HarvestForceAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),style,ct)));
        return app;
    }
    static async Task<bool> IsIronMineAsync(GameDb db,int mineId,CancellationToken ct){await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var q=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM world_mine_holdings WHERE mine_id=$1 AND mine_type IN(1,2))",c);q.Parameters.AddWithValue(mineId);return Convert.ToBoolean(await q.ExecuteScalarAsync(ct));}
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}
