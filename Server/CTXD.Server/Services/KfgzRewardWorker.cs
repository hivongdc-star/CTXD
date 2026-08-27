using CTXD.Server.Data;

namespace CTXD.Server.Services;

internal static class KfgzRewardWorker
{
    static int started;

    public static void Start(IServiceProvider services)
    {
        if(Interlocked.Exchange(ref started,1)!=0)return;
        var lifetime=services.GetRequiredService<IHostApplicationLifetime>();
        var logger=services.GetRequiredService<ILoggerFactory>().CreateLogger("KfgzRewardWorker");
        _=Task.Run(()=>RunAsync(services,logger,lifetime.ApplicationStopping),lifetime.ApplicationStopping);
    }

    static async Task RunAsync(IServiceProvider services,ILogger logger,CancellationToken ct)
    {
        var reward=new KfgzRewardService(services.GetRequiredService<GameDb>(),services.GetRequiredService<GamePushHub>());
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await reward.FinalizeEndedSeasonsAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(30),ct);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
            catch(Exception ex)
            {
                logger.LogError(ex,"KFGZ reward finalization iteration failed");
                try{await Task.Delay(TimeSpan.FromSeconds(30),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
            }
        }
    }
}
