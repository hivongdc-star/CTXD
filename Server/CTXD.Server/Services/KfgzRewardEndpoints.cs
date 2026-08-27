using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class KfgzRewardEndpoints
{
    public static IEndpointRouteBuilder MapKfgzRewardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/kfgz/rewards/round/{roundId:long}",async(long roundId,HttpRequest request,AuthService auth,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new KfgzRewardService(db,push).GetRoundAsync(player,roundId,ct));
        });
        app.MapPost("/api/kfgz/rewards/round/{roundId:long}/claim",async(long roundId,HttpRequest request,AuthService auth,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new KfgzRewardService(db,push).ClaimRoundAsync(player,roundId,ct));
        });
        app.MapGet("/api/kfgz/rewards/end",async(HttpRequest request,AuthService auth,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new KfgzRewardService(db,push).GetEndAsync(player,ct));
        });
        app.MapPost("/api/kfgz/rewards/end/{slot:int}/claim",async(int slot,HttpRequest request,AuthService auth,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new KfgzRewardService(db,push).ClaimEndAsync(player,slot,ct));
        });
        app.MapGet("/api/kfgz/titles",async(HttpRequest request,AuthService auth,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(new{items=await new KfgzRewardService(db,push).TitlesAsync(player,ct)});
        });

        app.MapPost("/internal/kfgz/rewards/round",async(KfgzRoundRewardProvision body,HttpRequest request,IConfiguration config,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            if(!InternalAuthorized(request,config))return InternalAuthResult(config);
            await new KfgzRewardService(db,push).ProvisionRoundRewardAsync(body,ct);return Results.Ok();
        });
        app.MapPost("/internal/kfgz/rewards/end-profile",async(KfgzEndRewardProfileProvision body,HttpRequest request,IConfiguration config,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            if(!InternalAuthorized(request,config))return InternalAuthResult(config);
            await new KfgzRewardService(db,push).ProvisionEndRewardProfileAsync(body,ct);return Results.Ok();
        });
        app.MapPost("/internal/kfgz/rewards/title-candidate",async(KfgzTitleCandidateProvision body,HttpRequest request,IConfiguration config,GameDb db,GamePushHub push,CancellationToken ct)=>
        {
            if(!InternalAuthorized(request,config))return InternalAuthResult(config);
            await new KfgzRewardService(db,push).ProvisionTitleCandidateAsync(body,ct);return Results.Ok();
        });
        return app;
    }

    static bool InternalAuthorized(HttpRequest request,IConfiguration config)
    {
        var expected=config["Game:BattleResultKey"];
        return !string.IsNullOrWhiteSpace(expected)&&string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal);
    }
    static IResult InternalAuthResult(IConfiguration config)=>string.IsNullOrWhiteSpace(config["Game:BattleResultKey"])?Results.StatusCode(503):Results.Unauthorized();
    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}
