using CTXD.Server.Data;

namespace CTXD.Server.Services;

public sealed record KfgzCallGeneralRequest(int[] GeneralIds);
public sealed record KfgzCallGeneralInfo(int CityId,int[] GeneralIds);
public sealed record KfgzCallGeneralFailure(int GeneralId,string Code,string Message);
public sealed record KfgzCallGeneralResult(int CityId,int[] MovedGeneralIds,KfgzCallGeneralFailure[] Failed);

public sealed class KfgzCallGeneralService(CanonicalContent content,KfgzService kfgz,KfgzReinforcementService reinforcement)
{
    public async Task<KfgzCallGeneralInfo> InfoAsync(long playerId,int cityId,CancellationToken ct)
    {
        var world=await kfgz.WorldAsync(playerId,ct);
        ValidateTarget(world,cityId);
        var ids=world.Deployments
            .Where(x=>x.PlayerId==playerId&&x.State==1&&x.CityId!=cityId)
            .Select(x=>x.GeneralId)
            .Distinct()
            .OrderBy(x=>x)
            .ToArray();
        if(ids.Length==0)throw new GameException("KFGZ_NO_GENERAL_TO_CALL","No idle general can be called to this city.",409);
        return new(cityId,ids);
    }

    public async Task<KfgzCallGeneralResult> CallAsync(long playerId,int cityId,KfgzCallGeneralRequest request,CancellationToken ct)
    {
        var ids=(request.GeneralIds??[]).Distinct().ToArray();
        if(ids.Length==0)throw new GameException("KFGZ_CALL_GENERAL_REQUIRED","Select at least one general to call.");

        var world=await kfgz.WorldAsync(playerId,ct);
        ValidateTarget(world,cityId);
        var known=world.Deployments.Where(x=>x.PlayerId==playerId).Select(x=>x.GeneralId).ToHashSet();
        if(ids.Any(x=>!known.Contains(x)))throw new GameException("KFGZ_CALL_GENERAL_INVALID","One or more selected generals are not synchronized into this KFGZ round.",400);
        var activeBattle=(world.Battles??[]).FirstOrDefault(x=>x.CityId==cityId&&x.State==1);

        var moved=new List<int>();var failed=new List<KfgzCallGeneralFailure>();
        foreach(var generalId in ids)
        {
            try
            {
                if(activeBattle is null)await kfgz.MoveAsync(playerId,generalId,cityId,ct);
                else await reinforcement.ReinforceAsync(playerId,activeBattle.BattleId,new KfgzReinforcementRequest([generalId]),ct);
                moved.Add(generalId);
            }
            catch(GameException ex)
            {
                failed.Add(new(generalId,ex.Code,ex.Message));
            }
        }
        return new(cityId,moved.ToArray(),failed.ToArray());
    }

    void ValidateTarget(KfgzWarView world,int cityId)
    {
        if(!content.KfgzWorldCities.TryGetValue(cityId,out var city)||city.World!=world.WorldId)
            throw new GameException("KFGZ_CALL_GENERAL_CITY_INVALID","Call-general target must be a city in the active KFGZ world.",404);
    }
}
