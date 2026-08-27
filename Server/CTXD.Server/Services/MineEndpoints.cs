using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class MineEndpoints
{
    public static IEndpointRouteBuilder MapMineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/world/mines",async(int? page,int? style,HttpRequest request,AuthService auth,MineService mines,GameDb db,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var mineStyle=style??1;var result=await mines.GetAsync(player,page??0,mineStyle,ct);
            if(mineStyle==1)await QuestEventLedger.RecordAsync(db,player,"world_mine_iron_visit",0,ct);
            return Results.Ok(result);
        });
        app.MapPost("/api/world/mines/occupy",async(MineOccupyRequest body,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>Results.Ok(await mines.OccupyAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),body.MineId,body.GeneralId,ct)));
        app.MapPost("/api/world/mines/rush/{style:int}",async(int style,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>{await mines.RushAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),style,ct);return Results.Ok();});
        app.MapPost("/api/world/mines/abandon/{style:int}",async(int style,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>Results.Ok(await mines.AbandonAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),style,ct)));
        app.MapPost("/api/world/mines/harvest/{style:int}",async(int style,HttpRequest request,AuthService auth,MineService mines,CancellationToken ct)=>Results.Ok(await mines.HarvestForceAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),style,ct)));
        return app;
    }
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}
