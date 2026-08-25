using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class DatabaseInitializer(GameDb db, IHostEnvironment env, ILogger<DatabaseInitializer> log)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        var dir = ResolveMigrationDirectory(env.ContentRootPath);
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        foreach (var file in Directory.EnumerateFiles(dir, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            log.LogInformation("Applying idempotent migration {Migration}", Path.GetFileName(file));
            await using var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(file, ct), conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    static string ResolveMigrationDirectory(string contentRoot)
    {
        var relative = Path.Combine("Database", "Migrations");
        var candidates = new[]
        {
            Path.Combine(contentRoot, relative),
            Path.Combine(AppContext.BaseDirectory, relative),
            Path.GetFullPath(Path.Combine(contentRoot, "..", "..", relative)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative))
        };
        return candidates.FirstOrDefault(Directory.Exists)
               ?? throw new DirectoryNotFoundException($"Migration directory not found. Tried: {string.Join(" | ", candidates)}");
    }
}
