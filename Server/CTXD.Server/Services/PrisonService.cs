using CTXD.Server.Data;
using CTXD.Server.Domain;

namespace CTXD.Server.Services;

public sealed record PrisonDegreeView(int Level,int ExtraExp,int ExtraCd,int Cost,int FreePointCap,int TotalPoint);
public sealed record PrisonerView(long Id,long SlavePlayerId,int GeneralId,string PlayerName,string GeneralName,string GeneralPic,int ForceId,int Level,int SlashTimes,DateTimeOffset GrabTime,DateTimeOffset? EscapeAt);
public sealed record CaptiveGeneralView(long Id,long HolderPlayerId,string HolderName,int GeneralId,string GeneralName,int SlashTimes,DateTimeOffset GrabTime,DateTimeOffset? EscapeAt);
public sealed record PrisonView(bool Built,bool HavePic,int PrisonLv,bool CanUpdate,bool HaveUpgradePic,int LashLv,int MaxLashLv,int ExtraExp,int ExtraCd,int UpgradeGold,int GrabNum,int Quality,long AutoLashExp,bool HaveTech,int CurrentFreePoint,int MaxFreePoint,int TotalPoint,PrisonDegreeView[] LashList,PrisonerView[] Generals,CaptiveGeneralView[] Captives);
public sealed record PrisonLashResult(long SlaveId,int RewardExp,int AddedEscapeSeconds,int LashLevel,int CurrentFreePoint);
public sealed record PrisonEscapeResult(long SlaveId,int GeneralId,int Seconds,DateTimeOffset EscapeAt);
public sealed record PrisonCaptureResult(bool Captured,double Probability,long? SlaveId);

public sealed partial class PrisonService
{
    readonly GameDb db;
    readonly CanonicalContent content;
    readonly IPlayerItemInventory items;
    readonly ExperienceService experience;
    readonly TechnologyEffectService technologies;
    readonly DstqActivityService dstq;
    readonly GamePushHub push;
    readonly StaticData data;

    const int FunctionId=52;
    const int DrawingItemType=8;
    const int CapturedState=22;
    const int EscapingState=23;
    const int IdleState=1;
    const int FreedomGold=5;

    public PrisonService(GameDb db,CanonicalContent content,IPlayerItemInventory items,ExperienceService experience,TechnologyEffectService technologies,DstqActivityService dstq,GamePushHub push)
    {
        this.db=db;this.content=content;this.items=items;this.experience=experience;this.technologies=technologies;this.dstq=dstq;this.push=push;
        data=Cache.GetOrAdd(content.BaseDirectory,LoadStatic);
    }
}
