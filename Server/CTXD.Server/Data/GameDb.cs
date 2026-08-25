using Npgsql;

namespace CTXD.Server.Data;

public sealed class GameDb : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; }
    public GameDb(IConfiguration cfg)
    {
        var cs=cfg.GetConnectionString("Game") ?? throw new InvalidOperationException("ConnectionStrings:Game missing");
        DataSource=NpgsqlDataSource.Create(cs);
    }
    public ValueTask DisposeAsync() => DataSource.DisposeAsync();
}
