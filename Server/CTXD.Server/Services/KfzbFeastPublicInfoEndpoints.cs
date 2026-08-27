namespace CTXD.Server.Services;

public static class KfzbFeastPublicInfoEndpoints
{
    public static Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapKfzbFeastPublicInfoEndpoints(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        app.MapGet("/api/kfzb/feast/info",async(HttpRequest request,AuthService auth,KfzbFeastService feast,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await feast.InfoAsync(id,ct));
        });
        return app;
    }

    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}
