using CTXD.Server.Data;

namespace CTXD.Server.Services;

internal static class MineWorker
{
    static int started;
    public static void Start(IServiceProvider services){if(Interlocked.Exchange(ref started,1)!=0)return;var lifetime=services.GetRequiredService<IHostApplicationLifetime>();var logger=services.GetRequiredService<ILoggerFactory>().CreateLogger("MineWorker");_=Task.Run(()=>RunAsync(services.GetRequiredService<MineService>(),logger,lifetime.ApplicationStopping),lifetime.ApplicationStopping);}
    static async Task RunAsync(MineService mines,ILogger logger,CancellationToken ct){while(!ct.IsCancellationRequested)try{await Task.Delay(TimeSpan.FromSeconds(10),ct);await mines.TickDueAsync(ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}catch(Exception ex){logger.LogError(ex,"Mine settlement iteration failed");}}
}
