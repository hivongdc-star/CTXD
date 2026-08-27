using CTXD.Server.Data;

namespace CTXD.Server.Services;

internal static class AutoBattleWorker
{
    static int started;

    public static void Start(IServiceProvider services)
    {
        if(Interlocked.Exchange(ref started,1)!=0)return;
        var lifetime=services.GetRequiredService<IHostApplicationLifetime>();
        var logger=services.GetRequiredService<ILoggerFactory>().CreateLogger("AutoBattleWorker");
        _=Task.Run(()=>RunAsync(services,logger,lifetime.ApplicationStopping),lifetime.ApplicationStopping);
    }

    static async Task RunAsync(IServiceProvider services,ILogger logger,CancellationToken ct)
    {
        var db=services.GetRequiredService<GameDb>();
        var content=services.GetRequiredService<CanonicalContent>();
        var technologies=services.GetRequiredService<TechnologyEffectService>();
        var production=services.GetRequiredService<ResourceProductionService>();
        var world=services.GetRequiredService<WorldService>();
        var battles=services.GetRequiredService<BattleService>();
        var push=services.GetRequiredService<GamePushHub>();
        var autoBattle=new AutoBattleService(db,content,technologies,production,world,battles);

        while(!ct.IsCancellationRequested)
        {
            try
            {
                // Legacy daemon wakes every 5 seconds while each player row is due every 10 seconds.
                await Task.Delay(TimeSpan.FromSeconds(5),ct);
                foreach(var playerId in await autoBattle.FindDuePlayersAsync(ct))
                {
                    try
                    {
                        var state=await autoBattle.TickAsync(playerId,ct);
                        if(state is not null)await push.SendAsync(playerId,"auto-battle.updated",state,ct);
                    }
                    catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
                    catch(Exception ex){logger.LogError(ex,"Auto Battle tick failed for player {PlayerId}",playerId);}
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogError(ex,"Auto Battle worker iteration failed");}
        }
    }
}
