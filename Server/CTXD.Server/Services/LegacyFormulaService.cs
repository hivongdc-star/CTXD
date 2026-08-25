using CTXD.Server.Data;
using CTXD.Server.Domain;

namespace CTXD.Server.Services;

public sealed class LegacyFormulaService(CanonicalContent content)
{
    // Ported from BuildingService.getBuildingUpradeCopperCost/getBuildingUpradeWoodCost/getBuildingUpgradeTime.
    public int CopperCost(BuildingDefinition b,int targetLevel) => (int)(b.CopperExponent * content.Serial(b.CopperSeriesId,targetLevel));
    public int WoodCost(BuildingDefinition b,int targetLevel) => (int)(b.WoodExponent * content.Serial(b.WoodSeriesId,targetLevel));
    public int UpgradeDurationMs(BuildingDefinition b,int targetLevel)
    {
        var n=b.TimeBase + content.Serial(b.TimeSeriesId,targetLevel) + content.Serial(b.TimeRSeriesId,targetLevel)*content.Serial(b.TimeTSeriesId,targetLevel);
        return (int)(b.TimeExponent*n*1000d);
    }
    public int ChiefExp(BuildingDefinition b,int level) => (int)Math.Floor(b.ChiefExpExponent * content.Serial(b.ChiefExpSeriesId,level));
}
