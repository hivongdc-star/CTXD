using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

internal static class KfgzMubingWorker
{
    static int started;

    public static void Start(IServiceProvider services)
    {
        if(Interlocked.Exchange(ref started,1)!=0)return;
        var lifetime=services.GetRequiredService<IHostApplicationLifetime>();
        var logger=services.GetRequiredService<ILoggerFactory>().CreateLogger("KfgzMubingWorker");
        _=Task.Run(()=>RunAsync(services,logger,lifetime.ApplicationStopping),lifetime.ApplicationStopping);
    }

    static async Task RunAsync(IServiceProvider services,ILogger logger,CancellationToken ct)
    {
        var db=services.GetRequiredService<GameDb>();
        var content=services.GetRequiredService<CanonicalContent>();
        var production=services.GetRequiredService<ResourceProductionService>();
        var technologies=services.GetRequiredService<TechnologyEffectService>();
        var push=services.GetRequiredService<GamePushHub>();
        var mubing=new KfgzMubingService(db,content,production,technologies,push);

        while(!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10),ct);
                var players=new List<long>();
                await using(var c=await db.DataSource.OpenConnectionAsync(ct))
                await using(var q=new NpgsqlCommand(@"
SELECT DISTINCT d.player_id
FROM kfgz_deployments d
JOIN kfgz_rounds r ON r.id=d.round_id
WHERE r.state=1 AND d.mubing_active=true
ORDER BY d.player_id",c))
                await using(var r=await q.ExecuteReaderAsync(ct))
                    while(await r.ReadAsync(ct))players.Add(r.GetInt64(0));
                foreach(var player in players)
                {
                    try{await mubing.TickPlayerAsync(player,ct);}
                    catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
                    catch(Exception ex){logger.LogError(ex,"KFGZ mubing tick failed for player {PlayerId}",player);}
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogError(ex,"KFGZ mubing worker iteration failed");}
        }
    }
}
