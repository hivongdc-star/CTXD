namespace CTXD.Server.Services;

public static class PrisonEndpoints
{
    public static IEndpointRouteBuilder MapPrisonEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/prison",async(HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).GetAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));
        app.MapPost("/api/prison/build",async(HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).BuildAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));
        app.MapPost("/api/prison/upgrade",async(HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).UpgradePrisonAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));
        app.MapPost("/api/prison/lash-level/upgrade",async(HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).UpgradeLashAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));
        app.MapPost("/api/prison/lash-level/trial",async(HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).UseTrialAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));
        app.MapPost("/api/prison/slaves/{slaveId:long}/lash",async(long slaveId,HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).LashAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),slaveId,ct)));
        app.MapPost("/api/prison/slaves/{slaveId:long}/freedom",async(long slaveId,HttpRequest request,AuthService auth,CancellationToken ct)=>{await Prison(request).ReleaseAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),slaveId,ct);return Results.Ok();});
        app.MapPost("/api/prison/captive/{generalId:int}/escape",async(int generalId,HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Prison(request).EscapeAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),generalId,ct)));
        return app;
    }
    static PrisonService Prison(HttpRequest request)=>PrisonService.FromServices(request.HttpContext.RequestServices);
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}
