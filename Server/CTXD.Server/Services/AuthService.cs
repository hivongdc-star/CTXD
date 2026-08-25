using System.Security.Cryptography;
using System.Text;
using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class AuthService(GameDb db, IConfiguration cfg, PlayerQueryService players)
{
    readonly int _sessionHours = cfg.GetValue("Game:SessionHours",72);

    public async Task<(string Token,PlayerView Player)> RegisterAsync(string username,string password,CancellationToken ct)
    {
        username=(username??"").Trim();
        password ??= "";
        if(username.Length is <3 or >64) throw new GameException("AUTH_USERNAME","Tên đăng nhập phải từ 3-64 ký tự.");
        if(password?.Length is <8 or >128) throw new GameException("AUTH_PASSWORD","Mật khẩu phải từ 8-128 ký tự.");
        await using var conn=await db.DataSource.OpenConnectionAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);
        try {
            long accountId;
            await using(var cmd=new NpgsqlCommand("INSERT INTO accounts(username,password_hash) VALUES($1,$2) RETURNING id",conn,tx))
            { cmd.Parameters.AddWithValue(username); cmd.Parameters.AddWithValue(PasswordService.Hash(password ?? "")); accountId=(long)(await cmd.ExecuteScalarAsync(ct))!; }
            long playerId;
            await using(var cmd=new NpgsqlCommand("INSERT INTO players(account_id) VALUES($1) RETURNING id",conn,tx))
            { cmd.Parameters.AddWithValue(accountId); playerId=(long)(await cmd.ExecuteScalarAsync(ct))!; }
            await using(var cmd=new NpgsqlCommand("INSERT INTO player_resources(player_id) VALUES($1)",conn,tx))
            { cmd.Parameters.AddWithValue(playerId); await cmd.ExecuteNonQueryAsync(ct); }
            var token=await CreateSessionAsync(conn,tx,accountId,ct);
            await tx.CommitAsync(ct);
            return (token,await players.GetPlayerAsync(playerId,ct));
        } catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation) {
            await tx.RollbackAsync(ct); throw new GameException("AUTH_EXISTS","Tên đăng nhập đã tồn tại.");
        }
    }

    public async Task<(string Token,PlayerView Player)> LoginAsync(string username,string password,CancellationToken ct)
    {
        await using var conn=await db.DataSource.OpenConnectionAsync(ct);
        long accountId; string hash; long playerId;
        await using(var cmd=new NpgsqlCommand("SELECT a.id,a.password_hash,p.id FROM accounts a JOIN players p ON p.account_id=a.id WHERE a.username=$1",conn))
        {
            cmd.Parameters.AddWithValue((username??"").Trim());
            await using var r=await cmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct)) throw new GameException("AUTH_INVALID","Sai tài khoản hoặc mật khẩu.");
            accountId=r.GetInt64(0); hash=r.GetString(1); playerId=r.GetInt64(2);
        }
        if(!PasswordService.Verify(password??"",hash)) throw new GameException("AUTH_INVALID","Sai tài khoản hoặc mật khẩu.");
        await using var tx=await conn.BeginTransactionAsync(ct);
        var token=await CreateSessionAsync(conn,tx,accountId,ct); await tx.CommitAsync(ct);
        return (token,await players.GetPlayerAsync(playerId,ct));
    }

    async Task<string> CreateSessionAsync(NpgsqlConnection conn,NpgsqlTransaction tx,long accountId,CancellationToken ct)
    {
        var raw=RandomNumberGenerator.GetBytes(32); var token=Convert.ToBase64String(raw).TrimEnd('=').Replace('+','-').Replace('/','_');
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using var cmd=new NpgsqlCommand("INSERT INTO sessions(token_hash,account_id,expires_at) VALUES($1,$2,now()+($3 || ' hours')::interval)",conn,tx);
        cmd.Parameters.AddWithValue(hash); cmd.Parameters.AddWithValue(accountId); cmd.Parameters.AddWithValue(_sessionHours); await cmd.ExecuteNonQueryAsync(ct);
        return token;
    }

    public async Task<long> ResolvePlayerIdAsync(string? token,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(token)) throw new GameException("AUTH_REQUIRED","Chưa đăng nhập.",401);
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using var conn=await db.DataSource.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand("SELECT p.id FROM sessions s JOIN players p ON p.account_id=s.account_id WHERE s.token_hash=$1 AND s.expires_at>now()",conn);
        cmd.Parameters.AddWithValue(hash); var v=await cmd.ExecuteScalarAsync(ct);
        if(v is null) throw new GameException("AUTH_EXPIRED","Phiên đăng nhập đã hết hạn.",401);
        return (long)v;
    }
}

public sealed class GameException(string code,string message,int status=400):Exception(message)
{ public string Code {get;}=code; public int Status {get;}=status; }
