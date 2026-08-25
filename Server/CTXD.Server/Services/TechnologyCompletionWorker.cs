namespace CTXD.Server.Services;

public sealed class TechnologyCompletionWorker(
    TechnologyService technologies,
    GeneralService generals,
    MainCityService mainCity,
    GamePushHub push,
    ILogger<TechnologyCompletionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var changed = await technologies.CompleteDueAsync(stoppingToken);
                foreach (var playerId in changed)
                {
                    // A completed technology can immediately change resource output/capacity and
                    // civil/military general slots. Push every currently ported affected projection.
                    await push.SendAsync(playerId, "technology.updated",
                        await technologies.GetAsync(playerId, 1, stoppingToken), stoppingToken);
                    await push.SendAsync(playerId, "generals.updated",
                        await generals.GetRosterAsync(playerId, stoppingToken), stoppingToken);
                    await push.SendAsync(playerId, "maincity.updated",
                        await mainCity.GetAsync(playerId, stoppingToken), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Technology completion tick failed");
            }
        }
    }
}
