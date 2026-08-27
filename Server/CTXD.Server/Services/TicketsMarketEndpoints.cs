namespace CTXD.Server.Services;

public static class TicketsMarketEndpoints
{
    public static IEndpointRouteBuilder MapTicketsMarketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tickets/market",async(HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Service(request).GetAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));
        app.MapPost("/api/tickets/market/{marketId:int}/buy",async(int marketId,TicketsBuyRequest body,HttpRequest request,AuthService auth,CancellationToken ct)=>Results.Ok(await Service(request).BuyAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),marketId,body.Quantity,ct)));
        return app;
    }
    static TicketsMarketService Service(HttpRequest request)=>TicketsMarketService.FromServices(request.HttpContext.RequestServices);
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}
