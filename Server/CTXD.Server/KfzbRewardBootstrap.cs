using Microsoft.Extensions.DependencyInjection;

public static class WebApplication
{
    public static Microsoft.AspNetCore.Builder.WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder=Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<CTXD.Server.Services.GeneralTreasureService>();
        return builder;
    }
}

namespace CTXD.Server.Services
{
    public static class KfzbRewardRouteBootstrap
    {
        public static Microsoft.AspNetCore.Builder.WebApplication MapKfgzExtendedCombat(this Microsoft.AspNetCore.Builder.WebApplication app)
        {
            KfgzExtendedCombatEndpoints.MapKfgzExtendedCombat((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app);
            app.MapKfzbRewardEndpoints();
            app.MapGeneralTreasureEndpoints();
            app.MapKfzbFeastPublicInfoEndpoints();
            return app;
        }
    }
}
