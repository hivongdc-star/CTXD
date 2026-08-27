namespace CTXD.Server.Services;

internal static class PrisonWorker
{
    static int started;
    public static void Start(IServiceProvider services)
    {
        if(Interlocked.Exchange(ref started,1)!=0)return;
        var lifetime=services.GetRequiredService<IHostApplicationLifetime>();
        var logger=services.GetRequiredService<ILoggerFactory>().CreateLogger("PrisonWorker");
        _=Task.Run(()=>RunAsync(PrisonService.FromServices(services),logger,lifetime.ApplicationStopping),lifetime.ApplicationStopping);
    }
    static async Task RunAsync(PrisonService prison,ILogger logger,CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try{await Task.Delay(TimeSpan.FromSeconds(1),ct);await prison.TickDueAsync(ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogError(ex,"Prison lifecycle iteration failed");}
        }
    }
}
