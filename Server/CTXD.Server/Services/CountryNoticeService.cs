using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record CountryNoticePayload(string Command,string Type,string Content);

internal sealed class CountryNoticeService
{
    const string LegacyModule="push@notice";
    const string LegacyCommand="notice";
    const string CountryType="COUNTRY";
    const int ResourceAdditionSubgroup=1;

    public async Task PublishForPlayerAsync(GameDb db,GamePushHub hub,long playerId,string content,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("SELECT force_id FROM players WHERE id=$1",c);
        q.Parameters.AddWithValue(playerId);
        var raw=await q.ExecuteScalarAsync(ct);
        if(raw is null or DBNull)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
        await PublishAsync(hub,Convert.ToInt16(raw),content,ResourceAdditionSubgroup,ct);
    }

    public Task PublishAsync(GamePushHub hub,short forceId,string content,int? subgroup,CancellationToken ct=default)
    {
        if(forceId is<1 or>3)throw new GameException("NOTICE_FORCE_INVALID","Country notice force id must be 1..3.");
        if(string.IsNullOrEmpty(content))throw new GameException("NOTICE_CONTENT_INVALID","Country notice content is required.");
        if(subgroup is not null and not(1 or 2))throw new GameException("NOTICE_SUBGROUP_INVALID","Legacy country subgroup must be 1 or 2.");
        return hub.SendCountryGroupAsync(forceId,subgroup,LegacyModule,new CountryNoticePayload(LegacyCommand,CountryType,content),ct);
    }
}
